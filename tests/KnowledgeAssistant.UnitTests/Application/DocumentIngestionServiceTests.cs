using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Application.Ingestion;
using KnowledgeAssistant.Infrastructure.Persistence;
using KnowledgeAssistant.TestSupport;
using KnowledgeAssistant.UnitTests.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Application;

public sealed class DocumentIngestionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly HashingEmbeddingGenerator _embeddings = new();
    private readonly IngestionOptions _options = new() { ChunkSizeCharacters = 200, ChunkOverlapCharacters = 40, EmbeddingBatchSize = 2 };

    private static readonly PageContent[] Pages =
    [
        new(2, "# Booking\n\nFlights must be booked 14 days in advance."),
        new(1, "# Scope\n\nThis policy applies to all employees."),
    ];

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task IngestAsync_StoresDocumentAndChunksWithCitationAndTenantMetadata()
    {
        var owner = TestUsers.Contributor("tenant-a");

        var document = await IngestAsync(owner, new IngestDocumentCommand("travel.pdf", "Travel Policy", Pages, ["HR.Reader"]));

        Assert.Equal(("tenant-a", owner.UserId, 2, 2), (document.TenantId, document.UploadedByUserId, document.PageCount, document.ChunkCount));
        Assert.Equal(["HR.Reader"], document.AllowedRoles);

        await using var db = _database.CreateContext();
        var chunks = await db.Chunks.Where(c => c.DocumentId == document.Id).OrderBy(c => c.ChunkIndex).ToListAsync();
        // Pages are processed in page order even when supplied out of order.
        Assert.Equal([(1, "Scope"), (2, "Booking")], chunks.Select(c => (c.PageNumber, c.Section!)));
        Assert.All(chunks, c =>
        {
            Assert.Equal("tenant-a", c.TenantId);
            Assert.Equal(_embeddings.ModelId, c.EmbeddingModel);
            Assert.NotEmpty(c.Embedding);
            Assert.Contains(c.Embedding, v => v != 0);
        });
    }

    [Fact]
    public async Task IngestAsync_EmbedsChunkTextWithTitleAndSectionContext_InBatches()
    {
        PageContent[] pages = [.. Enumerable.Range(1, 5).Select(i => new PageContent(i, $"# Part {i}\n\nBody of part {i}."))];

        await IngestAsync(TestUsers.Contributor(), new IngestDocumentCommand("guide.pdf", "Guide", pages, null));

        Assert.Equal(5, _embeddings.Inputs.Count);
        Assert.Equal("Guide — Part 3\n\nBody of part 3.", _embeddings.Inputs[2]);
    }

    [Fact]
    public async Task IngestAsync_SameContentTwiceInOneTenant_ThrowsConflict_ButAnotherTenantMayIngestIt()
    {
        var command = new IngestDocumentCommand("travel.pdf", null, Pages, null);
        await IngestAsync(TestUsers.Contributor("tenant-a"), command);

        await Assert.ThrowsAsync<ConflictException>(() => IngestAsync(TestUsers.Contributor("tenant-a"), command));
        await IngestAsync(TestUsers.Contributor("tenant-b"), command);

        await using var db = _database.CreateContext();
        Assert.Equal(2, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task Store_UniqueIndexTurnsARacedDuplicateIntoConflict()
    {
        var owner = TestUsers.Contributor();
        var first = TestDocuments.Create(owner);
        var racing = KnowledgeAssistant.Domain.Documents.Document.Create(
            owner, "copy.pdf", null, first.ContentHash, 1, 0, null, DateTimeOffset.UtcNow);

        await using var db = _database.CreateContext();
        var store = new EfDocumentRepository(db);
        await store.AddAsync(first, [], CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => store.AddAsync(racing, [], CancellationToken.None));
    }

    [Theory]
    [InlineData("no-text")]
    [InlineData("duplicate-pages")]
    [InlineData("too-many-pages")]
    [InlineData("too-many-characters")]
    [InlineData("role-too-long")]
    public async Task IngestAsync_InvalidContent_ThrowsValidationAndStoresNothing(string scenario)
    {
        _options.MaxPages = 3;
        _options.MaxTotalCharacters = 500;
        var command = scenario switch
        {
            "no-text" => new IngestDocumentCommand("a.pdf", null, [new PageContent(1, " \n ")], null),
            "duplicate-pages" => new IngestDocumentCommand("a.pdf", null, [new PageContent(1, "x"), new PageContent(1, "y")], null),
            "too-many-pages" => new IngestDocumentCommand("a.pdf", null, [.. Enumerable.Range(1, 4).Select(i => new PageContent(i, "x"))], null),
            "too-many-characters" => new IngestDocumentCommand("a.pdf", null, [new PageContent(1, new string('x', 501))], null),
            _ => new IngestDocumentCommand("a.pdf", null, [new PageContent(1, "text")], [new string('r', 129)]),
        };

        await Assert.ThrowsAsync<RequestValidationException>(() => IngestAsync(TestUsers.Contributor(), command));

        await using var db = _database.CreateContext();
        Assert.Equal(0, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFails_StoresNothing()
    {
        var failing = new FailingEmbeddingGenerator();

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => IngestAsync(TestUsers.Contributor(), new IngestDocumentCommand("a.pdf", null, Pages, null), failing));

        await using var db = _database.CreateContext();
        Assert.Equal(0, await db.Documents.CountAsync());
        Assert.Equal(0, await db.Chunks.CountAsync());
    }

    [Fact]
    public async Task Delete_RemovesChunksWithTheDocument_AndOnlyInTheOwningTenant()
    {
        var document = await IngestAsync(TestUsers.Contributor("tenant-a"), new IngestDocumentCommand("a.pdf", null, Pages, null));

        await using var db = _database.CreateContext();
        var store = new EfDocumentRepository(db);
        Assert.False(await store.DeleteAsync("tenant-b", document.Id, CancellationToken.None));
        Assert.Equal(2, await db.Chunks.CountAsync());

        Assert.True(await store.DeleteAsync("tenant-a", document.Id, CancellationToken.None));
        Assert.Equal(0, await db.Chunks.CountAsync());
    }

    private async Task<KnowledgeAssistant.Domain.Documents.Document> IngestAsync(
        KnowledgeAssistant.Domain.Security.UserContext user, IngestDocumentCommand command, IEmbeddingGenerator? embeddings = null)
    {
        await using var db = _database.CreateContext();
        var service = new DocumentIngestionService(
            new DocumentAwareChunker(Options.Create(_options)),
            embeddings ?? _embeddings,
            new EfDocumentRepository(db),
            TimeProvider.System,
            Options.Create(_options),
            NullLogger<DocumentIngestionService>.Instance);
        return await service.IngestAsync(user, command, CancellationToken.None);
    }

    private sealed class FailingEmbeddingGenerator : IEmbeddingGenerator
    {
        public string ModelId => "failing";

        public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            throw new LlmUnavailableException("The language model provider could not be reached.");
    }
}
