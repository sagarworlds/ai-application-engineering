using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Infrastructure.Persistence;

internal static class DocumentAccessQueryExtensions
{
    /// <summary>
    /// SQL translation of <see cref="Document.IsAccessibleTo"/>: same tenant, and unrestricted or
    /// role match or tenant admin. Kept in one place so every query applies the identical rule;
    /// <c>EfDocumentRepositoryTests</c> asserts it agrees with the domain rule.
    /// </summary>
    public static IQueryable<Document> WhereAccessibleTo(this IQueryable<Document> documents, UserContext user)
    {
        var tenantId = user.TenantId;
        var isAdmin = user.IsTenantAdmin;
        var roles = user.Roles.ToArray();

        return documents.Where(d =>
            d.TenantId == tenantId
            && (isAdmin || d.AllowedRoles.Count == 0 || d.AllowedRoles.Any(role => roles.Contains(role))));
    }
}
