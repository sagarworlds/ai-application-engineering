using KnowledgeAssistant.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace KnowledgeAssistant.UnitTests.TestSupport;

/// <summary>A throwaway SQLite file with the real schema, deleted on dispose.</summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"eka-unit-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<KnowledgeDbContext> _options;

    public SqliteTestDatabase()
    {
        // Pooling off so the file handle is released and the file can be deleted.
        _options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False")
            .Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public KnowledgeDbContext CreateContext() => new(_options);

    public void Dispose() => File.Delete(_path);
}
