using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class DocumentEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly UserContext Owner = new("dev-tenant", "seed-user", [AppRoles.Contributor]);

    [Fact]
    public async Task List_ReturnsSeededDocumentMetadata()
    {
        var document = NewDocument("travel-policy.pdf");
        await factory.SeedAsync(document);
        using var client = factory.CreateClientFor("dev-tenant", AppRoles.Reader);

        var page = await client.GetFromJsonAsync<PagedResponse<DocumentResponse>>("/api/v1/documents?pageSize=100");

        Assert.NotNull(page);
        var item = Assert.Single(page.Items, d => d.Id == document.Id);
        Assert.Equal("travel-policy.pdf", item.FileName);
        Assert.Equal(3, item.PageCount);
    }

    [Fact]
    public async Task Get_ExistingDocument_ReturnsMetadata_AndNeverExposesTenantId()
    {
        var document = NewDocument("handbook.pdf");
        await factory.SeedAsync(document);
        using var client = factory.CreateClientFor("dev-tenant", AppRoles.Reader);

        var json = await client.GetStringAsync($"/api/v1/documents/{document.Id}");

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(document.Id, parsed.RootElement.GetProperty("id").GetGuid());
        Assert.DoesNotContain("tenant", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_UnknownDocument_Returns404ProblemWithTraceId()
    {
        using var client = factory.CreateClientFor("dev-tenant", AppRoles.Reader);

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task List_InvalidPaging_Returns400Problem(string query)
    {
        using var client = factory.CreateClientFor("dev-tenant", AppRoles.Reader);

        var response = await client.GetAsync($"/api/v1/documents?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static Document NewDocument(string fileName) => Document.Create(
        Owner, fileName, title: null, contentHash: Guid.NewGuid().ToString("N"),
        pageCount: 3, chunkCount: 0, allowedRoles: null, DateTimeOffset.UtcNow);
}
