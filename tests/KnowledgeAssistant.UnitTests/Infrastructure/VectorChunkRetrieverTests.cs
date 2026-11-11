using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Questions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.Infrastructure.Persistence;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.TestSupport;
using KnowledgeAssistant.UnitTests.TestSupport;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Infrastructure;

public sealed class VectorChunkRetrieverTests : IDisposable
{
    private const string Question = "How many days in advance must flights be booked?";
    private readonly SqliteTestDatabase _database = new();
    private readonly HashingEmbeddingGenerator _embeddings = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SearchAsync_RanksTheMostSimilarChunkFirst_WithCitationMetadata()
    {
        var document = await SeedAsync("tenant-a", "travel.pdf", null,
            (3, "Booking", "Flights must be booked 14 days in advance."),
            (5, "Meals", "The daily meal allowance is 45 euros."));

        var hits = await SearchAsync(TestUsers.Reader("tenant-a"), Question);

        var best = hits[0];
        Assert.Equal(("travel.pdf", 3, "Booking"), (best.FileName, best.PageNumber, best.Section));
        Assert.Equal(document.Id, best.DocumentId);
        Assert.Equal("Flights must be booked 14 days in advance.", best.Content);
        Assert.True(hits.Zip(hits.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));
    }

    [Fact]
    public async Task SearchAsync_NeverReturnsAnotherTenantsChunks_EvenForIdenticalText()
    {
        await SeedAsync("tenant-b", "theirs.pdf", null, (1, null, "Flights must be booked 14 days in advance."));

        Assert.Empty(await SearchAsync(TestUsers.Reader("tenant-a"), Question));
        Assert.Empty(await SearchAsync(TestUsers.Admin("tenant-a"), Question));
        Assert.Single(await SearchAsync(TestUsers.Reader("tenant-b"), Question));
    }

    [Fact]
    public async Task SearchAsync_AppliesDocumentAclToChunks()
    {
        await SeedAsync("tenant-a", "board-travel.pdf", ["Board.Member"], (1, null, "Board flights must be booked 2 days in advance."));

        Assert.Empty(await SearchAsync(TestUsers.Reader("tenant-a"), Question));
        Assert.Single(await SearchAsync(TestUsers.Reader("tenant-a", "Board.Member"), Question));
        Assert.Single(await SearchAsync(TestUsers.Admin("tenant-a"), Question));
        Assert.Empty(await SearchAsync(TestUsers.Reader("tenant-b", "Board.Member"), Question));
    }

    [Fact]
    public async Task SearchAsync_IrrelevantChunksBelowMinScore_AreNotEvidence()
    {
        await SeedAsync("tenant-a", "menu.pdf", null, (1, null, "The cafeteria serves pasta on Tuesdays."));

        Assert.Empty(await SearchAsync(TestUsers.Reader("tenant-a"), Question));
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostTopK()
    {
        await SeedAsync("tenant-a", "many.pdf", null,
            [.. Enumerable.Range(1, 8).Select(i => (i, (string?)null, $"Flights must be booked {i} days in advance."))]);

        var hits = await SearchAsync(TestUsers.Reader("tenant-a"), Question, topK: 3);

        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public async Task SearchAsync_IgnoresChunksEmbeddedWithADifferentModel()
    {
        await SeedAsync("tenant-a", "old.pdf", null, (1, null, "Flights must be booked 14 days in advance."));

        var hits = await SearchAsync(TestUsers.Reader("tenant-a"), Question, embeddings: new HashingEmbeddingGenerator("new-model"));

        Assert.Empty(hits);
    }

    [Theory]
    [InlineData(new[] { 1f, 0f }, new[] { 2f, 0f }, 1.0)]
    [InlineData(new[] { 1f, 0f }, new[] { 0f, 3f }, 0.0)]
    [InlineData(new[] { 1f, 0f }, new[] { -1f, 0f }, -1.0)]
    public void CosineSimilarity_ComparesDirectionNotMagnitude(float[] a, float[] b, double expected)
    {
        Assert.Equal(expected, VectorMath.CosineSimilarity(a, b)!.Value, precision: 6);
    }

    [Theory]
    [InlineData(new[] { 1f, 0f }, new[] { 1f })]
    [InlineData(new[] { 0f, 0f }, new[] { 1f, 0f })]
    [InlineData(new float[0], new float[0])]
    public void CosineSimilarity_IncomparableVectors_ReturnsNull(float[] a, float[] b)
    {
        Assert.Null(VectorMath.CosineSimilarity(a, b));
    }

    [Fact]
    public void VectorBlob_RoundTripsExactly()
    {
        float[] vector = [0f, 1.5f, -2.25f, float.Epsilon, float.MaxValue];

        Assert.Equal(vector, VectorBlob.ToVector(VectorBlob.ToBytes(vector)));
    }

    private async Task<Document> SeedAsync(
        string tenantId, string fileName, string[]? allowedRoles, params (int Page, string? Section, string Text)[] passages)
    {
        var owner = new UserContext(tenantId, "seed", [AppRoles.Contributor]);
        var document = TestDocuments.Create(owner, fileName, allowedRoles);
        var chunks = passages.Select((p, i) => DocumentChunk.Create(
            document, i, p.Page, p.Section, p.Text, HashingEmbeddingGenerator.Embed(p.Text), _embeddings.ModelId)).ToList();

        await using var db = _database.CreateContext();
        await new EfDocumentRepository(db).AddAsync(document, chunks, CancellationToken.None);
        return document;
    }

    private async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        UserContext user, string query, int topK = 5, IEmbeddingGenerator? embeddings = null)
    {
        await using var db = _database.CreateContext();
        var retriever = new VectorChunkRetriever(
            db, embeddings ?? _embeddings, Options.Create(new RetrievalOptions { TopK = topK, MinScore = 0.25 }));
        return await retriever.SearchAsync(user, query, CancellationToken.None);
    }
}
