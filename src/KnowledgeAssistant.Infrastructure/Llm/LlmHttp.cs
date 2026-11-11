using System.Net.Http.Headers;
using System.Text.Json;

using KnowledgeAssistant.Application.Exceptions;

using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Infrastructure.Llm;

/// <summary>Transport concerns shared by the OpenAI-compatible chat and embedding clients.</summary>
internal static partial class LlmHttp
{
    /// <summary>Applies base address and credentials from <paramref name="options"/> to a typed client.</summary>
    public static void Configure(HttpClient client, LlmOptions options)
    {
        // Timeouts are enforced per call with linked cancellation tokens so a timeout can be told
        // apart from the caller cancelling; HttpClient's own timeout would blur the two.
        client.Timeout = Timeout.InfiniteTimeSpan;

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return;
        }

        // A trailing slash makes relative paths ("chat/completions") append instead of replacing the last segment.
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        if (string.Equals(options.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
        else
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
        }
    }

    /// <summary>Throws <see cref="LlmUnavailableException"/> for a non-success status.</summary>
    public static void EnsureSuccess(HttpResponseMessage response, ILogger logger, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The body is not logged or surfaced: providers may echo prompt text in error messages.
        LogProviderRejected(logger, operation, (int)response.StatusCode);
        throw new LlmUnavailableException("The language model provider rejected the request or is unavailable.");
    }

    /// <summary>
    /// Translates transport-level failures into application exceptions. Returns <c>null</c> for
    /// exceptions that should propagate unchanged — in particular the caller's own cancellation.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="callerToken">The token supplied by our caller (not the internal timeout token).</param>
    /// <param name="timeout">The timeout that applied to the call.</param>
    public static Exception? Translate(Exception exception, CancellationToken callerToken, TimeSpan timeout) => exception switch
    {
        // Our linked token fired but the caller's did not: that is the timeout.
        OperationCanceledException when !callerToken.IsCancellationRequested => new LlmTimeoutException(timeout, exception),
        HttpRequestException or IOException => new LlmUnavailableException("The language model provider could not be reached.", exception),
        JsonException => new LlmUnavailableException("The language model provider returned an unreadable response.", exception),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model provider rejected {Operation} with status {StatusCode}")]
    private static partial void LogProviderRejected(ILogger logger, string operation, int statusCode);
}
