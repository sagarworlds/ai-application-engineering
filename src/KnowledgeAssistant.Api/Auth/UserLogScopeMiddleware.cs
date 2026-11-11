namespace KnowledgeAssistant.Api.Auth;

/// <summary>
/// Adds tenant and user ids to the logging scope so every log line of a request can be traced
/// to a caller without logging any request content.
/// </summary>
internal sealed class UserLogScopeMiddleware(RequestDelegate next, ILogger<UserLogScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.ToUserContext() is not { } user)
        {
            await next(context);
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = user.TenantId,
            ["UserId"] = user.UserId,
        }))
        {
            await next(context);
        }
    }
}
