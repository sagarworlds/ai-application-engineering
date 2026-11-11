using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.Infrastructure.Persistence;
using KnowledgeAssistant.UnitTests.TestSupport;

namespace KnowledgeAssistant.UnitTests.Infrastructure;

public sealed class EfDocumentRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ListAccessibleAsync_ReturnsOnlyCallersTenant()
    {
        await SeedAsync(
            TestDocuments.Create(TestUsers.Contributor("tenant-a"), "a.pdf"),
            TestDocuments.Create(TestUsers.Contributor("tenant-b"), "b.pdf"));

        var (items, total) = await ListAsync(TestUsers.Reader("tenant-a"));

        Assert.Equal(1, total);
        Assert.Equal("a.pdf", Assert.Single(items).FileName);
    }

    [Fact]
    public async Task ListAccessibleAsync_SqlFilterAgreesWithDomainRule()
    {
        Document[] documents =
        [
            TestDocuments.Create(TestUsers.Contributor("tenant-a"), "open.pdf"),
            TestDocuments.Create(TestUsers.Contributor("tenant-a"), "finance.pdf", ["Finance.Reader"]),
            TestDocuments.Create(TestUsers.Contributor("tenant-a"), "hr-or-finance.pdf", ["HR.Reader", "Finance.Reader"]),
            TestDocuments.Create(TestUsers.Contributor("tenant-b"), "other-open.pdf"),
            TestDocuments.Create(TestUsers.Contributor("tenant-b"), "other-finance.pdf", ["Finance.Reader"]),
        ];
        await SeedAsync(documents);

        UserContext[] users =
        [
            TestUsers.Reader("tenant-a"),
            TestUsers.Reader("tenant-a", "Finance.Reader"),
            TestUsers.Reader("tenant-a", "HR.Reader"),
            TestUsers.Admin("tenant-a"),
            TestUsers.Reader("tenant-b", "Finance.Reader"),
            TestUsers.Admin("tenant-c"),
        ];

        foreach (var user in users)
        {
            var expected = documents.Where(d => d.IsAccessibleTo(user)).Select(d => d.FileName).Order();

            var (items, total) = await ListAsync(user);

            Assert.Equal(expected, items.Select(d => d.FileName).Order());
            Assert.Equal(items.Count, total);
        }
    }

    [Fact]
    public async Task ListAccessibleAsync_PagesNewestFirst()
    {
        var owner = TestUsers.Contributor();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync([.. Enumerable.Range(1, 5).Select(i =>
            TestDocuments.Create(owner, $"doc-{i}.pdf", createdAt: start.AddDays(i)))]);

        await using var db = _database.CreateContext();
        var (items, total) = await new EfDocumentRepository(db)
            .ListAccessibleAsync(TestUsers.Reader(), skip: 1, take: 2, CancellationToken.None);

        Assert.Equal(5, total);
        Assert.Equal(["doc-4.pdf", "doc-3.pdf"], items.Select(d => d.FileName));
    }

    [Fact]
    public async Task FindAccessibleAsync_OtherTenantOrMissingRole_ReturnsNull()
    {
        var restricted = TestDocuments.Create(TestUsers.Contributor("tenant-a"), "finance.pdf", ["Finance.Reader"]);
        await SeedAsync(restricted);

        await using var db = _database.CreateContext();
        var repository = new EfDocumentRepository(db);

        Assert.Null(await repository.FindAccessibleAsync(TestUsers.Admin("tenant-b"), restricted.Id, CancellationToken.None));
        Assert.Null(await repository.FindAccessibleAsync(TestUsers.Reader("tenant-a"), restricted.Id, CancellationToken.None));

        var found = await repository.FindAccessibleAsync(
            TestUsers.Reader("tenant-a", "Finance.Reader"), restricted.Id, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(restricted.CreatedAtUtc.ToUnixTimeMilliseconds(), found.CreatedAtUtc.ToUnixTimeMilliseconds());
        Assert.Equal(["Finance.Reader"], found.AllowedRoles);
    }

    private async Task SeedAsync(params Document[] documents)
    {
        await using var db = _database.CreateContext();
        db.Documents.AddRange(documents);
        await db.SaveChangesAsync();
    }

    private async Task<(IReadOnlyList<Document> Items, int Total)> ListAsync(UserContext user)
    {
        await using var db = _database.CreateContext();
        return await new EfDocumentRepository(db).ListAccessibleAsync(user, 0, 100, CancellationToken.None);
    }
}
