using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Infrastructure.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

/// <summary>Hosts the real API in-process against a throwaway SQLite database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"eka-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:KnowledgeDb", $"Data Source={_databasePath};Pooling=False");
    }

    /// <summary>Inserts documents directly, bypassing the API, to arrange test state.</summary>
    internal async Task SeedAsync(params Document[] documents)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.Documents.AddRange(documents);
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_databasePath);
        }
    }
}
