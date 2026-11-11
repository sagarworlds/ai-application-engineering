using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.RateLimiting;
using KnowledgeAssistant.Application.Abstractions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Endpoints;

internal static class QuestionEndpoints
{
    public static IEndpointRouteBuilder MapQuestionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/questions", AskAsync)
            .WithTags("Questions")
            .WithName("AskQuestion")
            .WithSummary("Answers a question from the caller's documents, with citations.")
            .RequireAuthorization(AuthorizationPolicies.QuestionsAsk)
            .RequireRateLimiting(ModelRateLimiting.PolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(EndpointLimits.SmallJsonBodyBytes))
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Ok<QuestionResponse>> AskAsync(
        QuestionRequest request,
        ICurrentUser currentUser,
        IQuestionAnswerer answerer,
        CancellationToken cancellationToken)
    {
        var answer = await answerer.AskAsync(
            currentUser.User, request.Question, request.ConversationId, cancellationToken);
        return TypedResults.Ok(answer.ToResponse());
    }
}
