using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

/// <summary>Mints JWTs the test host accepts, and deliberately broken ones it must reject.</summary>
public sealed class TestTokens
{
    public const string Issuer = "eka-tests";
    public const string Audience = "api://knowledge-assistant";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>Base64 signing key, in the format the JWT bearer configuration expects.</summary>
    public string SigningKeyBase64 => Convert.ToBase64String(_key);

    public string Create(
        string? tenantId,
        string userId,
        IEnumerable<string> roles,
        string roleClaimType = "roles",
        DateTime? expires = null,
        byte[]? signingKey = null,
        string audience = Audience)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (tenantId is not null)
        {
            claims.Add(new Claim("tid", tenantId));
        }

        claims.AddRange(roles.Select(role => new Claim(roleClaimType, role)));

        var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = expiry.AddHours(-1),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });
    }
}
