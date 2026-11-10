using System.Text.RegularExpressions;

using KnowledgeAssistant.Domain.Documents;

using Microsoft.EntityFrameworkCore;

namespace KnowledgeAssistant.Infrastructure.Persistence;

/// <summary>EF Core context for the development store (SQLite).</summary>
internal sealed partial class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeDbContext).Assembly);

        // snake_case columns so the physical schema matches docs/02-architecture.md.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.SetColumnName(WordBoundary().Replace(property.Name, "$1_$2").ToLowerInvariant());
        }
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
