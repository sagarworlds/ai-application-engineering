using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Ingestion;

/// <summary>
/// Splits pages along the document's own structure before falling back to size:
/// page → section (heading) → paragraph → sentence → words. A chunk never spans two pages or two
/// sections, so its page/section metadata is always exact and citations point to the right place.
/// </summary>
public sealed partial class DocumentAwareChunker(IOptions<IngestionOptions> options) : ITextChunker
{
    private const int MaxHeadingLength = 80;
    private const int MaxSectionLength = 200;
    private const string ParagraphSeparator = "\n\n";

    private readonly int _chunkSize = options.Value.ChunkSizeCharacters;
    private readonly int _overlap = options.Value.ChunkOverlapCharacters;

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageContent> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var chunks = new List<TextChunk>();
        string? section = null; // a section usually continues onto following pages

        foreach (var page in pages)
        {
            var packer = new ChunkPacker(_chunkSize, _overlap);
            foreach (var block in ReadBlocks(page.Text))
            {
                if (block.IsHeading)
                {
                    Emit(chunks, packer.Flush(), page.PageNumber, section);
                    section = block.Text;
                    continue;
                }

                var firstUnitOfParagraph = true;
                foreach (var unit in SplitToUnits(block.Text, packer.MaxUnitLength))
                {
                    var separator = firstUnitOfParagraph ? ParagraphSeparator : " ";
                    Emit(chunks, packer.Add(unit, separator), page.PageNumber, section);
                    firstUnitOfParagraph = false;
                }
            }

            Emit(chunks, packer.Flush(), page.PageNumber, section);
        }

        return chunks;
    }

    private static void Emit(List<TextChunk> chunks, string? content, int pageNumber, string? section)
    {
        if (content is not null)
        {
            chunks.Add(new TextChunk(chunks.Count, pageNumber, section, content));
        }
    }

    /// <summary>Splits page text into headings and paragraphs (runs of non-blank lines).</summary>
    private static IEnumerable<Block> ReadBlocks(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Replace("\f", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new StringBuilder();
        var previousLineBlank = true;
        var previousParagraphWasListItem = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                if (paragraph.Length > 0)
                {
                    yield return new Block(false, paragraph.ToString());
                    paragraph.Clear();
                }

                previousLineBlank = true;
                continue;
            }

            if (TryReadMarkdownHeading(line) is { } markdownTitle)
            {
                if (paragraph.Length > 0)
                {
                    yield return new Block(false, paragraph.ToString());
                    paragraph.Clear();
                }

                // Markdown headings are unambiguous markup, so they become metadata only.
                yield return new Block(true, markdownTitle);
                previousLineBlank = true; // text right after a heading starts a new paragraph
                previousParagraphWasListItem = false;
                continue;
            }

            var continuation = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;
            if (previousLineBlank
                && IsPlainTextHeading(line, continuation, NextNonBlankLine(lines, i + 1), previousParagraphWasListItem))
            {
                // A plain-text heading is a guess. It sets the section, but the line also stays in the
                // body below, so a wrong guess costs a section label and never loses document text.
                yield return new Block(true, line);
            }

            if (paragraph.Length == 0)
            {
                previousParagraphWasListItem = NumberedLine().IsMatch(line);
            }

            paragraph.Append(paragraph.Length > 0 ? "\n" : string.Empty).Append(line);
            previousLineBlank = false;
        }

        if (paragraph.Length > 0)
        {
            yield return new Block(false, paragraph.ToString());
        }
    }

    /// <summary>
    /// Parses an ATX Markdown heading ("## Title", optionally closed with " ##"). Parsed by hand in
    /// one pass: this runs on every line of uploaded text, and a backtracking regex here could be
    /// driven to quadratic time by a crafted line.
    /// </summary>
    /// <returns>The heading title (capped at <see cref="MaxSectionLength"/>), or <c>null</c> when the line is not a heading.</returns>
    private static string? TryReadMarkdownHeading(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is 0 or > 6 || level == line.Length || !char.IsWhiteSpace(line[level]))
        {
            return null;
        }

        var title = line.AsSpan(level).Trim();

        // A closing run of #s counts only when separated by whitespace, so "# C#" keeps its title.
        var withoutClosing = title.TrimEnd('#');
        if (withoutClosing.Length < title.Length && (withoutClosing.Length == 0 || char.IsWhiteSpace(withoutClosing[^1])))
        {
            title = withoutClosing.TrimEnd();
        }

        if (title.Length == 0)
        {
            return null;
        }

        // The section is copied onto every chunk beneath it (rows, embedding input, prompts,
        // citations), so an unbounded heading would multiply its own size by the chunk count.
        return title.Length <= MaxSectionLength ? title.ToString() : title[..MaxSectionLength].ToString();
    }

    /// <summary>
    /// Heading heuristics for extracted plain text: a short line at the start of a paragraph, without
    /// sentence punctuation, that is either numbered ("2.1 Booking") or ALL CAPS. Numbered list
    /// items and the first line of a wrapped sentence are excluded.
    /// </summary>
    /// <param name="line">Candidate line (trimmed).</param>
    /// <param name="continuation">The line directly below, or empty when blank or absent.</param>
    /// <param name="nextNonBlankLine">The next line that has text, if any.</param>
    /// <param name="previousParagraphWasListItem">Whether the preceding paragraph began like "1. ...".</param>
    private static bool IsPlainTextHeading(
        string line, string continuation, string? nextNonBlankLine, bool previousParagraphWasListItem)
    {
        if (line.Length > MaxHeadingLength || ".,;:!?".Contains(line[^1], StringComparison.Ordinal))
        {
            return false;
        }

        // "…budgets were cut by" / "10 percent across all teams": the sentence simply wrapped.
        if (continuation.Length > 0 && (char.IsLower(continuation[0]) || char.IsDigit(continuation[0])))
        {
            return false;
        }

        if (NumberedHeading().IsMatch(line))
        {
            var isListItem = NumberedLine().IsMatch(line)
                && (previousParagraphWasListItem || (nextNonBlankLine is not null && NumberedLine().IsMatch(nextNonBlankLine)));
            return !isListItem;
        }

        return line.Count(char.IsLetter) >= 3 && !line.Any(char.IsLower);
    }

    private static string? NextNonBlankLine(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return lines[i].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Breaks a paragraph into units no longer than <paramref name="maxLength"/>: the paragraph
    /// itself when it fits, otherwise its sentences, otherwise word-wrapped pieces of a sentence.
    /// </summary>
    private static IEnumerable<string> SplitToUnits(string paragraph, int maxLength)
    {
        if (paragraph.Length <= maxLength)
        {
            yield return paragraph;
            yield break;
        }

        foreach (var sentence in SentenceBoundary().Split(paragraph))
        {
            var remaining = sentence.AsMemory().Trim();
            while (remaining.Length > maxLength)
            {
                // Prefer to cut at the last whitespace; a single "word" longer than the limit is cut hard.
                var cut = remaining.Span[..maxLength].LastIndexOfAny(' ', '\n', '\t');
                if (cut <= 0)
                {
                    cut = maxLength;
                }

                yield return remaining[..cut].TrimEnd().ToString();
                remaining = remaining[cut..].TrimStart();
            }

            if (remaining.Length > 0)
            {
                yield return remaining.ToString();
            }
        }
    }

    [GeneratedRegex(@"^\d+(\.\d+)*\.?\s+\p{Lu}")]
    private static partial Regex NumberedHeading();

    [GeneratedRegex(@"^\d+[.)]\s")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundary();

    private readonly record struct Block(bool IsHeading, string Text);

    /// <summary>Greedily packs units into chunks of at most <c>chunkSize</c> characters with overlap.</summary>
    private sealed class ChunkPacker(int chunkSize, int overlap)
    {
        private readonly StringBuilder _current = new();

        /// <summary>
        /// Longest unit that still fits behind an overlap tail and a separator. Guarantees that
        /// starting a new chunk with "tail + unit" never exceeds the chunk size.
        /// </summary>
        public int MaxUnitLength { get; } = chunkSize - overlap - ParagraphSeparator.Length;

        /// <summary>Adds a unit; returns a finished chunk when the unit did not fit into the current one.</summary>
        public string? Add(string unit, string separator)
        {
            if (_current.Length == 0)
            {
                _current.Append(unit);
                return null;
            }

            if (_current.Length + separator.Length + unit.Length <= chunkSize)
            {
                _current.Append(separator).Append(unit);
                return null;
            }

            var finished = _current.ToString();
            var tail = OverlapTail(finished);
            _current.Clear().Append(tail);
            if (tail.Length > 0)
            {
                _current.Append(' ');
            }

            _current.Append(unit);
            return finished;
        }

        /// <summary>Returns the pending chunk, if any, and resets (no overlap is carried across a flush).</summary>
        public string? Flush()
        {
            var pending = _current.Length > 0 ? _current.ToString() : null;
            _current.Clear();
            return pending;
        }

        private string OverlapTail(string chunk)
        {
            if (overlap == 0 || chunk.Length <= overlap)
            {
                // A chunk shorter than the overlap would be repeated in full; skip overlap instead.
                return string.Empty;
            }

            var tail = chunk.AsSpan(chunk.Length - overlap);

            // Start the overlap at a word boundary so the next chunk does not begin mid-word.
            var firstSpace = tail.IndexOfAny(' ', '\n', '\t');
            return firstSpace < 0 ? string.Empty : tail[(firstSpace + 1)..].TrimStart().ToString();
        }
    }
}
