namespace KnowledgeAssistant.Application.Ingestion;

/// <summary>Builds the text that is embedded for a chunk.</summary>
internal static class EmbeddingText
{
    /// <summary>
    /// Prefixes the passage with its document title and section. A chunk such as "It must be booked
    /// 14 days ahead" says nothing about <em>what</em> must be booked; the title and heading restore
    /// that context in the vector without changing the stored passage text.
    /// </summary>
    public static string ForChunk(string title, string? section, string content) =>
        string.IsNullOrWhiteSpace(section)
            ? $"{title}\n\n{content}"
            : $"{title} — {section}\n\n{content}";
}
