using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.Auth;

/// <summary>
/// Placeholder identity used until token authentication is added: every request runs as one
/// fixed local user. Replaced by a claims-based implementation in the security step.
/// </summary>
internal sealed class DevelopmentCurrentUser : ICurrentUser
{
    public UserContext User { get; } = new("dev-tenant", "dev-user", [AppRoles.Admin]);
}
