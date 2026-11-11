namespace KnowledgeAssistant.Domain.Security;

/// <summary>
/// The authenticated caller, as established from a validated token. This is the only source of
/// tenant identity in the system: it is never built from request bodies, routes or model output.
/// </summary>
public sealed record UserContext
{
    /// <summary>Creates a user context.</summary>
    /// <param name="tenantId">Tenant the caller belongs to. Must be non-empty.</param>
    /// <param name="userId">Stable identifier of the caller. Must be non-empty.</param>
    /// <param name="roles">Roles granted to the caller.</param>
    /// <exception cref="ArgumentException">A required identifier is null or whitespace.</exception>
    public UserContext(string tenantId, string userId, IEnumerable<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(roles);

        TenantId = tenantId;
        UserId = userId;
        Roles = roles.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Tenant the caller belongs to.</summary>
    public string TenantId { get; }

    /// <summary>Stable identifier of the caller within the identity provider.</summary>
    public string UserId { get; }

    /// <summary>Roles granted to the caller (case-sensitive, as issued).</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>True when the caller administers their own tenant.</summary>
    public bool IsTenantAdmin => Roles.Contains(AppRoles.Admin);
}
