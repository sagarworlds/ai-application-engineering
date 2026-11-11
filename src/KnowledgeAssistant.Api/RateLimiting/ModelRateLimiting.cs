using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Application.Common;

namespace KnowledgeAssistant.Api.RateLimiting;

/// <summary>Rate-limit settings (configuration section <c>RateLimiting</c>).</summary>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Model-backed requests (chat, questions, ingestion) allowed per user per minute.</summary>
    [Range(1, 100_000)]
    public int ModelRequestsPerMinute { get; set; } = 60;
}

internal static class ModelRateLimiting
{
    /// <summary>Policy for endpoints that spend model tokens.</summary>
    public const string PolicyName = "model-backed";

    /// <summary>
    /// Limits how often one user can call endpoints that cost money at the model provider, so a
    /// single account (or a leaked token) cannot run up the bill or starve other tenants.
    /// </summary>
    public static IServiceCollection AddModelRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<RateLimitOptions>(RateLimitOptions.SectionName);

        var permitsPerMinute = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()?.ModelRequestsPerMinute
            ?? new RateLimitOptions().ModelRequestsPerMinute;

        services.AddRateLimiter(options =>
        {
            // The empty 429 is given a problem+json body by the status-code-pages middleware.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, httpContext =>
            {
                // Partition per tenant + user: one noisy user cannot exhaust a colleague's budget.
                // The policy runs after authorization, so a user context is always present here.
                var user = httpContext.User.ToUserContext();
                var partition = user is null ? "anonymous" : $"{user.TenantId}\u001f{user.UserId}";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
