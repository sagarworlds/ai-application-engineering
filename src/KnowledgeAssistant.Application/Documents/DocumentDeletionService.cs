using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Security;

using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Application.Documents;

/// <summary>Deletes documents (and their chunks) on behalf of a caller.</summary>
public sealed partial class DocumentDeletionService(
    IDocumentRepository documents,
    IDocumentStore store,
    ILogger<DocumentDeletionService> logger)
{
    /// <summary>Deletes a document the caller can access.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="NotFoundException">The document does not exist or the caller may not access it.</exception>
    public async Task DeleteAsync(UserContext user, Guid documentId, CancellationToken cancellationToken)
    {
        // Resolve through the access rule first: a caller cannot delete what they are not allowed to see.
        var document = await documents.FindAccessibleAsync(user, documentId, cancellationToken).ConfigureAwait(false);
        if (document is null
            || !await store.DeleteAsync(user.TenantId, document.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException($"Document '{documentId}' was not found.");
        }

        LogDeleted(logger, documentId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted document {DocumentId}")]
    private static partial void LogDeleted(ILogger logger, Guid documentId);
}
