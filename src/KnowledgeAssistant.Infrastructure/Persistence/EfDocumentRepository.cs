using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

using Microsoft.EntityFrameworkCore;

namespace KnowledgeAssistant.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IDocumentRepository"/>.</summary>
internal sealed class EfDocumentRepository(KnowledgeDbContext db) : IDocumentRepository
{
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
