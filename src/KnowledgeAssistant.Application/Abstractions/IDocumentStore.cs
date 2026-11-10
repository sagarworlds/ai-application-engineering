using KnowledgeAssistant.Domain.Documents;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>Write-side persistence port for documents and their chunks.</summary>
public interface IDocumentStore
{
    /// <summary>Checks whether the tenant already holds a document with this content.</summary>
    /// <param name="tenantId">Tenant to look in.</param>
    /// <param name="contentHash">SHA-256 hex digest of the content.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when a document with the same content exists in the tenant.</returns>
    Task<bool> ContentExistsAsync(string tenantId, string contentHash, CancellationToken cancellationToken);

    /// <summary>Stores a document and all of its chunks atomically.</summary>
    /// <param name="document">Document metadata.</param>
    /// <param name="chunks">Chunks with embeddings; all must belong to <paramref name="document"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Exceptions.ConflictException">The same content was stored concurrently in this tenant.</exception>
    Task AddAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);

    /// <summary>Deletes a document and its chunks.</summary>
    /// <param name="tenantId">Tenant that owns the document.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when a document was deleted; false when none matched in that tenant.</returns>
    Task<bool> DeleteAsync(string tenantId, Guid documentId, CancellationToken cancellationToken);
}
