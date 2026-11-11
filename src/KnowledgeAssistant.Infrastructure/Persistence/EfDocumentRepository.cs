using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAssistant.Infrastructure.Persistence;

/// <summary>EF Core implementation of the document read and write ports.</summary>
internal sealed class EfDocumentRepository(KnowledgeDbContext db) : IDocumentRepository, IDocumentStore
{
    // SQLITE_CONSTRAINT: raised when the unique (tenant, content hash) index rejects a duplicate.
    private const int SqliteConstraintErrorCode = 19;

    public Task<bool> ContentExistsAsync(string tenantId, string contentHash, CancellationToken cancellationToken) =>
        db.Documents.AnyAsync(d => d.TenantId == tenantId && d.ContentHash == contentHash, cancellationToken);

    public async Task AddAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Any(c => c.DocumentId != document.Id || c.TenantId != document.TenantId))
        {
            throw new ArgumentException("Every chunk must belong to the document being stored.", nameof(chunks));
        }

        db.Documents.Add(document);
        db.Chunks.AddRange(chunks);

        try
        {
            // One SaveChanges = one transaction: the document and all its chunks, or nothing.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintErrorCode })
        {
            // Two identical uploads raced past the ContentExists check; the index is the real guard.
            throw new ConflictException("A document with identical content already exists.");
        }
    }

    public async Task<bool> DeleteAsync(string tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        // Chunks are removed by the database's ON DELETE CASCADE.
        var deleted = await db.Documents
            .Where(d => d.TenantId == tenantId && d.Id == documentId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return deleted > 0;
    }

    public async Task<Document?> FindAccessibleAsync(
        UserContext user, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(d => d.TenantId == user.TenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        // The tenant filter above is the hard boundary; the domain rule then applies the role ACL.
        return document is not null && document.IsAccessibleTo(user) ? document : null;
    }

    public async Task<(IReadOnlyList<Document> Items, int TotalCount)> ListAccessibleAsync(
        UserContext user, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.Documents.AsNoTracking().WhereAccessibleTo(user);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenBy(d => d.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }
}
