using System.Net;
using System.Net.Http.Json;

using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Domain.Security;

using Microsoft.AspNetCore.Hosting;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class RateLimitingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ModelBackedEndpoints_AreLimitedPerUser_NotPerTenantOrGlobally()
    {
        using var limited = factory.WithWebHostBuilder(
            builder => builder.UseSetting("RateLimiting:ModelRequestsPerMinute", "2"));
        using var alice = Authenticated(limited.CreateClient(), "alice");
        using var bob = Authenticated(limited.CreateClient(), "bob");
        var message = new { message = "hi" };

        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsJsonAsync("/api/v1/chat", message)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsJsonAsync("/api/v1/chat", message)).StatusCode);

        var rejected = await alice.PostAsJsonAsync("/api/v1/chat", message);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        await rejected.ReadProblemAsync();

        // Another user of the same tenant has an untouched budget, and cheap reads are not limited.
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsJsonAsync("/api/v1/chat", message)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/documents")).StatusCode);
    }

    private HttpClient Authenticated(HttpClient client, string userId)
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", factory.Tokens.Create("tenant-rate", userId, [AppRoles.Reader]));
        return client;
    }
}
