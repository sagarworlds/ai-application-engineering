using System.Security.Claims;

using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.Auth;

/// <summary>
/// Claim names the API relies on. They are the raw JWT names (inbound claim mapping is disabled)
/// so the same code reads Microsoft Entra ID tokens and local <c>dotnet user-jwts</c> tokens.
/// </summary>
internal static class TokenClaims
{
    /// <summary>Tenant identifier (Entra ID: directory tenant id).</summary>
    public const string TenantId = "tid";

    /// <summary>Immutable user object id (Entra ID).</summary>
    public const string ObjectId = "oid";

    /// <summary>Standard subject claim; used when <see cref="ObjectId"/> is absent.</summary>
    public const string Subject = "sub";

    /// <summary>Role claim names: <c>roles</c> (Entra app roles) and <c>role</c> (user-jwts).</summary>
    public static readonly string[] RoleTypes = ["roles", "role"];

    // Matches the identifier column size in the store; anything longer is not a real tenant/user id.
    private const int MaxIdentifierLength = 128;

    /// <summary>Builds the caller's <see cref="UserContext"/> from validated token claims.</summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The user context, or <c>null</c> when the token lacks a usable tenant or user id.</returns>
    public static UserContext? ToUserContext(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var tenantId = principal.FindFirstValue(TenantId);
        var userId = principal.FindFirstValue(ObjectId) ?? principal.FindFirstValue(Subject);
        if (!IsUsableIdentifier(tenantId) || !IsUsableIdentifier(userId))
        {
            return null;
        }

        var roles = principal.Claims.Where(c => RoleTypes.Contains(c.Type)).Select(c => c.Value);
        return new UserContext(tenantId!, userId!, roles);
    }

    private static bool IsUsableIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxIdentifierLength;
}
