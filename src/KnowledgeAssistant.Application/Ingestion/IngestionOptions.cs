using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Ingestion;

/// <summary>Chunking and ingestion limits (configuration section <c>Ingestion</c>).</summary>
public sealed class IngestionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Maximum chunk length in characters (~4 characters per token, so 1200 ≈ 300 tokens).
    /// Smaller chunks retrieve more precisely; larger chunks carry more context per hit.
    /// </summary>
    [Range(200, 8000)]
    public int ChunkSizeCharacters { get; set; } = 1200;

    /// <summary>
    /// Characters repeated from the end of one chunk at the start of the next, so a fact that
    /// straddles a boundary is fully contained in at least one chunk.
    /// </summary>
    [Range(0, 2000)]
    public int ChunkOverlapCharacters { get; set; } = 200;

    /// <summary>Maximum pages per document.</summary>
    [Range(1, 10_000)]
    public int MaxPages { get; set; } = 500;

    /// <summary>Maximum total characters per document; bounds synchronous ingestion time and embedding cost.</summary>
    [Range(1, 50_000_000)]
    public int MaxTotalCharacters { get; set; } = 2_000_000;

    /// <summary>
    /// Maximum chunks per document. Bounds embedding calls (provider cost) and the rows every later
    /// search in the tenant has to scan, however the text is structured.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxChunksPerDocument { get; set; } = 5000;

    /// <summary>Number of chunks sent to the embedding model per request.</summary>
    [Range(1, 512)]
    public int EmbeddingBatchSize { get; set; } = 64;
}

/// <summary>Cross-field validation of <see cref="IngestionOptions"/>.</summary>
internal sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions options) =>
        // With overlap above half the chunk size, most of every chunk would be duplicated text
        // and the chunker could no longer guarantee forward progress per chunk.
        options.ChunkOverlapCharacters * 2 > options.ChunkSizeCharacters
            ? ValidateOptionsResult.Fail("Ingestion:ChunkOverlapCharacters must be at most half of Ingestion:ChunkSizeCharacters.")
            : ValidateOptionsResult.Success;
}
