using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.RateLimiting;
using KnowledgeAssistant.Application.Chat;
using KnowledgeAssistant.Application.Exceptions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Endpoints;

internal static partial class ChatEndpoints
{
    /// <summary>SSE event carrying a fragment of the reply.</summary>
    public const string DeltaEvent = "delta";

    /// <summary>SSE event marking normal completion.</summary>
    public const string DoneEvent = "done";

    /// <summary>SSE event sent when generation fails after the stream has started.</summary>
    public const string ErrorEvent = "error";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/chat")
            .WithTags("Chat")
            .RequireAuthorization(AuthorizationPolicies.QuestionsAsk)
            .RequireRateLimiting(ModelRateLimiting.PolicyName)
            // Far above the 8,000-character message limit; stops huge bodies before they are buffered.
            .WithMetadata(new RequestSizeLimitAttribute(EndpointLimits.SmallJsonBodyBytes))
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/", CompleteAsync)
            .WithName("Chat")
            .WithSummary("Sends a message to the model and returns the full reply.")
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPost("/stream", StreamAsync)
            .WithName("ChatStream")
            .WithSummary("Streams the model's reply as server-sent events (delta, done, error).");

        return app;
    }

    private static async Task<Ok<ChatResponse>> CompleteAsync(
        ChatRequest request, ChatService chat, CancellationToken cancellationToken)
    {
        // cancellationToken is HttpContext.RequestAborted: a client disconnect cancels the upstream model call.
        var completion = await chat.CompleteAsync(request.Message, request.Temperature, cancellationToken);
        var usage = completion.Usage is { } u ? new UsageResponse(u.InputTokens, u.OutputTokens) : null;
        return TypedResults.Ok(new ChatResponse(completion.Content, completion.Model, usage));
    }

    private static async Task<ServerSentEventsResult<string>> StreamAsync(
        ChatRequest request, ChatService chat, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var deltas = chat.StreamAsync(request.Message, request.Temperature, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        // The response owns the upstream stream: it is released when the response ends, even if the
        // event stream below is never enumerated (e.g. the client aborts right after the handler returns).
        httpContext.Response.RegisterForDisposeAsync(deltas);

        // Pull the first fragment before committing to a 200 event stream. Failures that happen
        // up front (not configured, provider down) then surface as normal problem responses.
        var hasFirst = await deltas.MoveNextAsync();

        var logger = loggerFactory.CreateLogger(typeof(ChatEndpoints).FullName!);
        return TypedResults.ServerSentEvents(ToEventsAsync(deltas, hasFirst, logger, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> ToEventsAsync(
        IAsyncEnumerator<string> deltas,
        bool hasCurrent,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (hasCurrent)
        {
            yield return new SseItem<string>(deltas.Current, DeltaEvent);

            string? failure = null;
            try
            {
                hasCurrent = await deltas.MoveNextAsync();
            }
            catch (Exception exception) when (exception is LlmUnavailableException or LlmTimeoutException)
            {
                // Headers are already sent, so the status code cannot change any more.
                // Tell the client in-band instead of silently truncating the reply.
                LogStreamInterrupted(logger, exception);
                failure = "The reply was interrupted. Please try again.";
                hasCurrent = false;
            }

            if (failure is not null)
            {
                yield return new SseItem<string>(failure, ErrorEvent);
                yield break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return new SseItem<string>(string.Empty, DoneEvent);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chat stream was interrupted after it started")]
    private static partial void LogStreamInterrupted(ILogger logger, Exception exception);
}
