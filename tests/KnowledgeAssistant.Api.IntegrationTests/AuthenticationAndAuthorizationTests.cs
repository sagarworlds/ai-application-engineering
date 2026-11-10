using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class AuthenticationAndAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Documents = "/api/v1/documents";

    [Theory]
    [InlineData("GET", Documents)]
    [InlineData("GET", Documents + "/7b0e0f0e-7d0b-4c59-9a55-6f1b1a2f3c4d")]
    [InlineData("POST", "/api/v1/questions")]
    public async Task ProtectedEndpoint_WithoutToken_Returns401Problem(string method, string path)
    {
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task Token_SignedWithUnknownKey_Returns401()
    {
        var forged = factory.Tokens.Create("tenant-a", "mallory", [AppRoles.Admin], signingKey: RandomNumberGenerator.GetBytes(32));
        using var client = factory.CreateClientWithToken(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Documents)).StatusCode);
    }

    [Fact]
    public async Task Token_Expired_Returns401()
    {
        var expired = factory.Tokens.Create("tenant-a", "alice", [AppRoles.Reader], expires: DateTime.UtcNow.AddMinutes(-30));
        using var client = factory.CreateClientWithToken(expired);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Documents)).StatusCode);
    }

    [Fact]
    public async Task Token_ForAnotherAudience_Returns401()
    {
        var wrongAudience = factory.Tokens.Create("tenant-a", "alice", [AppRoles.Reader], audience: "api://some-other-api");
        using var client = factory.CreateClientWithToken(wrongAudience);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Documents)).StatusCode);
    }

    [Fact]
    public async Task Token_WithoutAnyRole_Returns403Problem()
    {
        using var client = factory.CreateClientFor("tenant-a");

        var response = await client.GetAsync(Documents);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task Token_WithUnrelatedRole_Returns403()
    {
        using var client = factory.CreateClientFor("tenant-a", "Some.Other.Role");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Documents)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/questions", new { question = "What is the policy?" })).StatusCode);
    }

    [Fact]
    public async Task Token_WithRoleButNoTenantClaim_Returns403()
    {
        var noTenant = factory.Tokens.Create(tenantId: null, "alice", [AppRoles.Admin]);
        using var client = factory.CreateClientWithToken(noTenant);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Documents)).StatusCode);
    }

    [Theory]
    [InlineData("roles")] // Microsoft Entra ID app roles
    [InlineData("role")]  // dotnet user-jwts
    public async Task Reader_WithEitherRoleClaimType_CanListDocuments(string roleClaimType)
    {
        var token = factory.Tokens.Create("tenant-a", "alice", [AppRoles.Reader], roleClaimType);
        using var client = factory.CreateClientWithToken(token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Documents)).StatusCode);
    }
}
