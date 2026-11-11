using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.Auth;

/// <summary>Gives endpoints the authenticated caller for the current request.</summary>
public interface ICurrentUser
{
    /// <summary>The caller's identity, tenant and roles.</summary>
    /// <exception cref="InvalidOperationException">The request has no usable identity.</exception>
    UserContext User { get; }
}
