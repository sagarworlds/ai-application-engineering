namespace KnowledgeAssistant.Api.Endpoints;

internal static class EndpointLimits
{
    /// <summary>Body limit for endpoints whose payload is a short JSON message (chat, questions).</summary>
    public const long SmallJsonBodyBytes = 64 * 1024;
}
