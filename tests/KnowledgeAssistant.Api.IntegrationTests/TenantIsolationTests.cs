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

    [Fact]
    public async Task Question_IsNeverAnsweredFromAnotherTenantsDocuments()
    {
        factory.ChatModel.Reset();
        factory.ChatModel.Reply = _ => SampleDocuments.AnswerCitingSourceOne;
        var owner = $"tenant-{Guid.NewGuid():N}";
        var outsider = $"tenant-{Guid.NewGuid():N}";
        using var contributor = factory.CreateClientFor(owner, AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());

        // Even an admin of another tenant, asking exactly what the document answers, gets nothing:
        // no chunk is retrieved, so the model is never called and cannot leak anything.
        using var outsiderAdmin = factory.CreateClientFor(outsider, AppRoles.Admin);
        var response = await outsiderAdmin.PostAsJsonAsync(
            "/api/v1/questions", new { question = "How many days in advance must flights be booked?" });

        var answer = await response.Content.ReadFromJsonAsync<QuestionResponse>();
        Assert.False(answer!.AnswerFound);
        Assert.Empty(answer.Citations);
        Assert.Empty(factory.ChatModel.Requests);
    }

    [Fact]
    public async Task Question_DoesNotUseRoleRestrictedDocumentsTheCallerCannotRead()
    {
        factory.ChatModel.Reset();
        factory.ChatModel.Reply = _ => SampleDocuments.AnswerCitingSourceOne;
        var tenant = $"tenant-{Guid.NewGuid():N}";
        using var contributor = factory.CreateClientFor(tenant, AppRoles.Contributor, "Board.Member");
        await contributor.IngestAsync(SampleDocuments.TravelPolicy(allowedRoles: ["Board.Member"]));
        var question = new { question = "How many days in advance must flights be booked?" };

        using var reader = factory.CreateClientFor(tenant, AppRoles.Reader);
        var denied = await (await reader.PostAsJsonAsync("/api/v1/questions", question)).Content.ReadFromJsonAsync<QuestionResponse>();
        Assert.False(denied!.AnswerFound);
        Assert.Empty(factory.ChatModel.Requests);

        using var boardMember = factory.CreateClientFor(tenant, AppRoles.Reader, "Board.Member");
        var allowed = await (await boardMember.PostAsJsonAsync("/api/v1/questions", question)).Content.ReadFromJsonAsync<QuestionResponse>();
        Assert.True(allowed!.AnswerFound);
    }

    [Fact]
    public async Task Delete_AnotherTenantsDocument_Returns404_AndLeavesItIntact()
    {
        var contoso = await SeedAsync("contoso", "keep-me.pdf");

        using var outsider = factory.CreateClientFor("fabrikam", AppRoles.Admin);
        var response = await outsider.DeleteAsync($"/api/v1/documents/{contoso.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var owner = factory.CreateClientFor("contoso", AppRoles.Reader);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/documents/{contoso.Id}")).StatusCode);
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
