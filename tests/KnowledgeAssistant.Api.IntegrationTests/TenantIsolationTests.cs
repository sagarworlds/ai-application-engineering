using System.Net;
using System.Net.Http.Json;

using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

/// <summary>
/// The tenant boundary and document ACL, exercised through the real HTTP + auth pipeline.
/// Any change to data access must keep these green.
/// </summary>
public sealed class TenantIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task List_NeverContainsAnotherTenantsDocuments()
    {
        var contoso = await SeedAsync("contoso", "contoso-handbook.pdf");
        var fabrikam = await SeedAsync("fabrikam", "fabrikam-handbook.pdf");

        using var client = factory.CreateClientFor("contoso", AppRoles.Reader);
        var page = await client.GetFromJsonAsync<PagedResponse<DocumentResponse>>("/api/v1/documents?pageSize=100");

        Assert.NotNull(page);
        Assert.Contains(page.Items, d => d.Id == contoso.Id);
        Assert.DoesNotContain(page.Items, d => d.Id == fabrikam.Id);
    }

    [Theory]
    [InlineData(AppRoles.Reader)]
    [InlineData(AppRoles.Admin)]
    public async Task Get_AnotherTenantsDocument_Returns404_EvenForAdmins(string role)
    {
        var contoso = await SeedAsync("contoso", "secret-plan.pdf");

        using var outsider = factory.CreateClientFor("fabrikam", role);
        var response = await outsider.GetAsync($"/api/v1/documents/{contoso.Id}");

        // 404, not 403: a 403 would confirm that the id exists in another tenant.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RoleRestrictedDocument_IsHiddenFromTenantMembersWithoutTheRole()
    {
        var restricted = await SeedAsync("contoso", "salaries.pdf", "HR.Reader");

        using var reader = factory.CreateClientFor("contoso", AppRoles.Reader);
        using var hrReader = factory.CreateClientFor("contoso", AppRoles.Reader, "HR.Reader");
        using var admin = factory.CreateClientFor("contoso", AppRoles.Admin);
        using var otherTenantHr = factory.CreateClientFor("fabrikam", AppRoles.Reader, "HR.Reader");

        var path = $"/api/v1/documents/{restricted.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hrReader.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenantHr.GetAsync(path)).StatusCode);

        var readerList = await reader.GetFromJsonAsync<PagedResponse<DocumentResponse>>("/api/v1/documents?pageSize=100");
        Assert.DoesNotContain(readerList!.Items, d => d.Id == restricted.Id);
    }

    private async Task<Document> SeedAsync(string tenantId, string fileName, params string[] allowedRoles)
    {
        var owner = new UserContext(tenantId, $"seed@{tenantId}", [AppRoles.Contributor]);
        var document = Document.Create(
            owner, fileName, title: null, contentHash: Guid.NewGuid().ToString("N"),
            pageCount: 1, chunkCount: 0, allowedRoles, DateTimeOffset.UtcNow);
        await factory.SeedAsync(document);
        return document;
    }
}
