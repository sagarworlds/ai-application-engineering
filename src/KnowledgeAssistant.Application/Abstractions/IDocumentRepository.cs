using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>
/// Persistence port for document metadata. Every member is scoped to a caller or tenant;
/// there is intentionally no way to query across tenants.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>Finds a document the caller is allowed to read.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The document, or <c>null</c> when it does not exist or is not accessible to the caller.</returns>
    Task<Document?> FindAccessibleAsync(UserContext user, Guid documentId, CancellationToken cancellationToken);

    /// <summary>Lists documents the caller is allowed to read, newest first.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="skip">Number of documents to skip.</param>
    /// <param name="take">Maximum number of documents to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The requested slice and the total number of accessible documents.</returns>
    Task<(IReadOnlyList<Document> Items, int TotalCount)> ListAccessibleAsync(
        UserContext user, int skip, int take, CancellationToken cancellationToken);
}
