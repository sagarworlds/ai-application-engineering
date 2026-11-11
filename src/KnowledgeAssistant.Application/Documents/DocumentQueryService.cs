using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Documents;

/// <summary>Read-side use cases for document metadata.</summary>
public sealed class DocumentQueryService(IDocumentRepository documents)
{
    /// <summary>Largest page a caller may request.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Gets a single document's metadata.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The document.</returns>
    /// <exception cref="NotFoundException">The document does not exist or the caller may not read it.</exception>
    public async Task<Document> GetAsync(UserContext user, Guid documentId, CancellationToken cancellationToken)
    {
        return await documents.FindAccessibleAsync(user, documentId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Document '{documentId}' was not found.");
    }

    /// <summary>Lists documents visible to the caller, newest first.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size, 1..<see cref="MaxPageSize"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One page of documents.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Paging arguments are out of range.</exception>
    public async Task<PagedResult<Document>> ListAsync(
        UserContext user, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        // long arithmetic: page is caller-controlled and (page - 1) * pageSize can overflow int.
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var (items, total) = await documents.ListAccessibleAsync(user, skip, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<Document>(items, page, pageSize, total);
    }
}
