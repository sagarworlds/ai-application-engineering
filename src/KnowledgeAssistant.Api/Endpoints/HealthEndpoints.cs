using KnowledgeAssistant.Infrastructure;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace KnowledgeAssistant.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: the process is up. Runs no dependency checks so a slow database
        // never causes the orchestrator to restart a healthy instance.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        // Readiness: dependencies needed to serve traffic are reachable.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DependencyInjection.ReadinessTag),
        }).AllowAnonymous();

        return app;
    }
}
