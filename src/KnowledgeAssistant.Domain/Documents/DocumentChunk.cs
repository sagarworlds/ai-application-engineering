namespace KnowledgeAssistant.Domain.Documents;

/// <summary>
/// A searchable passage of a document together with the metadata needed to cite it
/// (page, section) and to keep it inside its tenant.
/// </summary>
public sealed class DocumentChunk
{
    // Required by EF Core materialisation.
    private DocumentChunk()
    {
    }

    /// <summary>Unique identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Owning document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Tenant of the owning document, copied onto the chunk so searches can filter on it
    /// directly instead of depending on a join being written correctly.
    /// </summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>0-based position of the chunk within the document.</summary>
    public int ChunkIndex { get; private set; }

    /// <summary>1-based page the passage came from.</summary>
    public int PageNumber { get; private set; }

    /// <summary>Nearest preceding section heading, when one was detected.</summary>
    public string? Section { get; private set; }

    /// <summary>Passage text.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Embedding vector of the passage.</summary>
    public float[] Embedding { get; private set; } = [];

    /// <summary>Model that produced <see cref="Embedding"/>. Vectors from different models are not comparable.</summary>
    public string EmbeddingModel { get; private set; } = string.Empty;

    /// <summary>Creates a chunk belonging to <paramref name="document"/>.</summary>
    /// <param name="document">Owning document; supplies the tenant.</param>
    /// <param name="chunkIndex">0-based position within the document.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="section">Section heading, if any.</param>
    /// <param name="content">Passage text.</param>
    /// <param name="embedding">Embedding vector (non-empty).</param>
    /// <param name="embeddingModel">Model that produced the vector.</param>
    /// <returns>The new chunk.</returns>
    /// <exception cref="ArgumentException">Text, vector or model is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An index is out of range.</exception>
    public static DocumentChunk Create(
        Document document,
        int chunkIndex,
        int pageNumber,
        string? section,
        string content,
        float[] embedding,
        string embeddingModel)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("An embedding vector must not be empty.", nameof(embedding));
        }

        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            TenantId = document.TenantId,
            ChunkIndex = chunkIndex,
            PageNumber = pageNumber,
            Section = string.IsNullOrWhiteSpace(section) ? null : section.Trim(),
            Content = content,
            Embedding = embedding,
            EmbeddingModel = embeddingModel,
        };
    }
}
