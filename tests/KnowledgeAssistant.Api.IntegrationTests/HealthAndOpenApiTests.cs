using System.Net;
using System.Text.Json;

using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class HealthAndOpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_WithoutToken_ReturnsHealthy(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_DescribesAllEndpoints()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("/api/v1/documents", paths);
        Assert.Contains("/api/v1/documents/{id}", paths);
        Assert.Contains("/api/v1/questions", paths);
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemForAuthenticatedCaller_And401ForAnonymous()
    {
        using var anonymous = factory.CreateClient();
        using var reader = factory.CreateClientFor("tenant-a", AppRoles.Reader);

        // Deny-by-default also covers routes that do not exist, so anonymous callers cannot probe the API surface.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/does-not-exist")).StatusCode);

        var response = await reader.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await response.ReadProblemAsync();
    }
}
