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

            if (TryReadHeading(line, previousLineBlank, NextNonBlankLine(lines, i + 1)) is { } heading)
            {
                if (paragraph.Length > 0)
                {
                    yield return new Block(false, paragraph.ToString());
                    paragraph.Clear();
                }

                yield return new Block(true, heading);
                previousLineBlank = true; // text right after a heading starts a new paragraph
                continue;
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
    /// Heading heuristics for extracted text. Markdown headings are unambiguous. Plain-text headings
    /// (numbered "2.1 Booking" or ALL CAPS) must stand at the start of a paragraph, be short and
    /// carry no sentence punctuation; a numbered line followed by another numbered line is a list.
    /// </summary>
    private static string? TryReadHeading(string line, bool startsParagraph, string? nextLine)
    {
        if (MarkdownHeading().Match(line) is { Success: true } markdown)
        {
            return markdown.Groups["title"].Value;
        }

        if (!startsParagraph || line.Length > MaxHeadingLength || ".,;:!?".Contains(line[^1], StringComparison.Ordinal))
        {
            return null;
        }

        if (NumberedHeading().IsMatch(line))
        {
            return nextLine is not null && NumberedLine().IsMatch(nextLine) ? null : line;
        }

        var letters = line.Count(char.IsLetter);
        return letters >= 3 && !line.Any(char.IsLower) ? line : null;
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

    [GeneratedRegex(@"^#{1,6}\s+(?<title>.+?)\s*#*$")]
    private static partial Regex MarkdownHeading();

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
