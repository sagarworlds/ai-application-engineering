using KnowledgeAssistant.Domain.Security;

using Microsoft.AspNetCore.Authorization;

namespace KnowledgeAssistant.Api.Auth;

/// <summary>Named authorization policies (RBAC). Endpoints reference these, never raw role names.</summary>
internal static class AuthorizationPolicies
{
    /// <summary>Read document metadata.</summary>
    public const string DocumentsRead = "Documents.Read";

    /// <summary>Ingest and delete documents.</summary>
    public const string DocumentsWrite = "Documents.Write";

    /// <summary>Chat and ask questions.</summary>
    public const string QuestionsAsk = "Questions.Ask";

    public static IServiceCollection AddKnowledgeAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Deny by default: an endpoint without an explicit policy still needs a valid token.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(DocumentsRead, policy => RequireTenantMemberWithAnyRole(
                policy, AppRoles.Reader, AppRoles.Contributor, AppRoles.Admin))
            .AddPolicy(DocumentsWrite, policy => RequireTenantMemberWithAnyRole(
                policy, AppRoles.Contributor, AppRoles.Admin))
            .AddPolicy(QuestionsAsk, policy => RequireTenantMemberWithAnyRole(
                policy, AppRoles.Reader, AppRoles.Contributor, AppRoles.Admin));

        return services;
    }

    private static void RequireTenantMemberWithAnyRole(AuthorizationPolicyBuilder policy, params string[] roles)
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            // A token with a role but no tenant is useless to us: every query is tenant-scoped.
            context.User.ToUserContext() is { } user && roles.Any(user.Roles.Contains));
    }
}
