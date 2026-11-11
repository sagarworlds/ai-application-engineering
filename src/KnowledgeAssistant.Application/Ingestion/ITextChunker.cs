namespace KnowledgeAssistant.Application.Ingestion;

/// <summary>Text extracted from one page of a document.</summary>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="Text">Extracted text; may contain Markdown or plain headings.</param>
public sealed record PageContent(int PageNumber, string Text);

/// <summary>A passage produced by chunking, before it is embedded.</summary>
/// <param name="Index">0-based position within the document.</param>
/// <param name="PageNumber">Page the passage came from.</param>
/// <param name="Section">Nearest preceding heading, if any.</param>
/// <param name="Content">Passage text.</param>
public sealed record TextChunk(int Index, int PageNumber, string? Section, string Content);

/// <summary>Splits extracted pages into passages suitable for embedding and citation.</summary>
public interface ITextChunker
{
    /// <summary>Chunks <paramref name="pages"/> in the order given.</summary>
    /// <param name="pages">Pages in reading order.</param>
    /// <returns>Chunks in reading order; empty when the pages contain no text.</returns>
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageContent> pages);
}
