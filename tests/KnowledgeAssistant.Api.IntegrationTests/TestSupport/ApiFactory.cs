using System.Net.Http.Headers;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Infrastructure.Persistence;
using KnowledgeAssistant.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

/// <summary>Hosts the real API in-process against a throwaway SQLite database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"eka-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:KnowledgeDb", $"Data Source={_databasePath};Pooling=False");

        // Same validation pipeline as production; only the trust anchor (a per-run symmetric key) differs.
        const string bearer = "Authentication:Schemes:Bearer";
        builder.UseSetting($"{bearer}:ValidIssuer", TestTokens.Issuer);
        builder.UseSetting($"{bearer}:ValidAudiences:0", TestTokens.Audience);
        builder.UseSetting($"{bearer}:SigningKeys:0:Issuer", TestTokens.Issuer);
        builder.UseSetting($"{bearer}:SigningKeys:0:Value", Tokens.SigningKeyBase64);

        // Tests share a handful of users; the per-user limit has its own test (RateLimitingTests).
        builder.UseSetting("RateLimiting:ModelRequestsPerMinute", "100000");

        // No network in tests: model ports are replaced by fakes.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatModel>();
            services.AddSingleton<IChatModel>(ChatModel);
            services.RemoveAll<IEmbeddingGenerator>();
            services.AddSingleton<IEmbeddingGenerator>(Embeddings);
        });
    }

    public TestTokens Tokens { get; } = new();

    /// <summary>The chat model the API talks to in tests.</summary>
    public FakeChatModel ChatModel { get; } = new();

    /// <summary>Deterministic embedding model used by ingestion and retrieval in tests.</summary>
    public HashingEmbeddingGenerator Embeddings { get; } = new();

    /// <summary>Creates a client authenticated as a user of <paramref name="tenantId"/> with the given roles.</summary>
    public HttpClient CreateClientFor(string tenantId, params string[] roles) =>
        CreateClientWithToken(Tokens.Create(tenantId, $"user-{string.Join('-', roles)}@{tenantId}", roles));

    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
