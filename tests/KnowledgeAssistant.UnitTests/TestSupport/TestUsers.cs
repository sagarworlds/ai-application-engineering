using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.UnitTests.TestSupport;

internal static class TestUsers
{
    public static UserContext Reader(string tenant = "tenant-a", params string[] extraRoles) =>
        new(tenant, $"reader@{tenant}", [AppRoles.Reader, .. extraRoles]);

    public static UserContext Contributor(string tenant = "tenant-a") =>
        new(tenant, $"contributor@{tenant}", [AppRoles.Contributor]);

    public static UserContext Admin(string tenant = "tenant-a") =>
        new(tenant, $"admin@{tenant}", [AppRoles.Admin]);
}

internal static class TestDocuments
{
    public static Document Create(
        UserContext owner, string fileName = "handbook.pdf", string[]? allowedRoles = null, DateTimeOffset? createdAt = null) =>
        Document.Create(
            owner,
            fileName,
            title: null,
            contentHash: Guid.NewGuid().ToString("N"),
            pageCount: 1,
            chunkCount: 0,
            allowedRoles,
            createdAt ?? DateTimeOffset.UtcNow);
}
