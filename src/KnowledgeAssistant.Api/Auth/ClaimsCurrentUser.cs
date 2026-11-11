using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.Auth;

/// <summary>Resolves the caller from the current request's validated token.</summary>
internal sealed class ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private UserContext? _user;

    public UserContext User => _user ??= httpContextAccessor.HttpContext?.User.ToUserContext()
        // Unreachable for endpoints behind an authorization policy (they all require a tenant
        // claim); failing loudly beats silently running without a tenant.
        ?? throw new InvalidOperationException("The current request has no authenticated user with a tenant.");
}
