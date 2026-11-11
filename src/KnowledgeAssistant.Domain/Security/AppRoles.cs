namespace KnowledgeAssistant.Domain.Security;

/// <summary>Application roles carried in the token's role claims (Entra ID app roles in production).</summary>
public static class AppRoles
{
    /// <summary>May read document metadata, chat and ask questions.</summary>
    public const string Reader = "Knowledge.Reader";

    /// <summary>Reader permissions plus ingesting and deleting documents.</summary>
    public const string Contributor = "Knowledge.Contributor";

    /// <summary>Full access inside the caller's own tenant, including role-restricted documents.</summary>
    public const string Admin = "Knowledge.Admin";
}
