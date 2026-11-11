using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Llm;

/// <summary>
/// <see cref="IChatModel"/> over the OpenAI-compatible <c>POST chat/completions</c> API.
/// Created through <see cref="IHttpClientFactory"/> as a typed client.
/// </summary>
internal sealed partial class OpenAiCompatibleChatModel(
    HttpClient httpClient,
    IOptions<LlmOptions> options,
    ILogger<OpenAiCompatibleChatModel> logger) : IChatModel
{
    private const string Operation = "chat completion";
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options = options.Value;

    public async Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        // Linked source: cancels when the caller cancels OR when the timeout elapses.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        var started = Stopwatch.GetTimestamp();

        try
        {
            using var message = BuildRequest(request, stream: false);
            using var response = await httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            LlmHttp.EnsureSuccess(response, logger, Operation);

            var payload = await response.Content
                .ReadFromJsonAsync<CompletionPayload>(PayloadOptions, timeout.Token)
                .ConfigureAwait(false);

            var choice = payload?.Choices?.FirstOrDefault();
            if (choice?.Message?.Content is not { Length: > 0 } content)
            {
                throw new LlmUnavailableException("The language model provider returned an empty reply.");
            }

            var usage = payload!.Usage is { } u ? new TokenUsage(u.PromptTokens, u.CompletionTokens) : null;
            LogCompleted(logger, payload.Model ?? _options.ChatModel, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                usage?.InputTokens, usage?.OutputTokens, choice.FinishReason);

            return new ChatCompletion(content, payload.Model ?? _options.ChatModel, usage, choice.FinishReason);
        }
        catch (Exception exception) when (LlmHttp.Translate(exception, cancellationToken, _options.RequestTimeout) is { } translated)
        {
            throw translated;
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.StreamTimeout);

        // C# cannot `yield` inside a try block that has a catch clause, so the enumerator is
        // advanced manually: failures are translated in the try, values are yielded outside it.
        await using var deltas = ReadDeltasAsync(request, timeout.Token).GetAsyncEnumerator(timeout.Token);
        while (true)
        {
            string delta;
            try
            {
                if (!await deltas.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                delta = deltas.Current;
            }
            catch (Exception exception) when (LlmHttp.Translate(exception, cancellationToken, _options.StreamTimeout) is { } translated)
            {
                throw translated;
            }

            yield return delta;
        }
    }

    private async IAsyncEnumerable<string> ReadDeltasAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = BuildRequest(request, stream: true);
        // ResponseHeadersRead: start consuming as soon as headers arrive instead of buffering the whole stream.
        using var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        LlmHttp.EnsureSuccess(response, logger, Operation);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);

            // Server-sent events: each event is a line "data: {json}"; the stream ends with "data: [DONE]".
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue; // blank separators, comments, other SSE fields
                }

                var data = line.AsSpan(5).Trim();
                if (data.SequenceEqual("[DONE]"))
                {
                    yield break;
                }

                var chunk = JsonSerializer.Deserialize<StreamPayload>(data, PayloadOptions);
                if (chunk?.Error is not null)
                {
                    throw new LlmUnavailableException("The language model provider reported an error while streaming.");
                }

                if (chunk?.Choices?.FirstOrDefault()?.Delta?.Content is { Length: > 0 } text)
                {
                    yield return text;
                }
            }

            // The body ended without the terminating [DONE] event: the reply was cut off upstream.
            // Reporting that as success would present a truncated answer as complete.
            throw new LlmUnavailableException("The language model stream ended unexpectedly.");
        }
    }

    private HttpRequestMessage BuildRequest(ChatCompletionRequest request, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = _options.ChatModel,
            ["messages"] = new JsonArray([.. request.Messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    _ => "user",
                },
                ["content"] = m.Content,
            })]),
        };

        if (request.Temperature is { } temperature)
        {
            body["temperature"] = temperature;
        }

        if (request.MaxOutputTokens is { } maxTokens)
        {
            body[_options.MaxTokensParameterName] = maxTokens;
        }

        if (stream)
        {
            body["stream"] = true;
        }

        if (request.ResponseFormat is { } format)
        {
            AddResponseFormat(body, format);
        }

        return new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
    }

    private void AddResponseFormat(JsonObject body, JsonSchemaFormat format)
    {
        switch (_options.StructuredOutputMode)
        {
            case StructuredOutputMode.JsonSchema:
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = format.Name,
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(format.SchemaJson),
                    },
                };
                break;
            case StructuredOutputMode.JsonObject:
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
                break;
            case StructuredOutputMode.None:
            default:
                break; // the prompt alone describes the format; the server-side parser still validates
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsChatConfigured)
        {
            // Which settings are missing is an operator concern; it goes to the log, not to API clients.
            LogNotConfigured(logger);
            throw new LlmUnavailableException("The language model is not configured.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat model is not configured: set Llm:BaseUrl and Llm:ChatModel")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Chat completion by {Model} took {ElapsedMs:0} ms (input {InputTokens} tokens, output {OutputTokens} tokens, finish {FinishReason})")]
    private static partial void LogCompleted(
        ILogger logger, string model, double elapsedMs, int? inputTokens, int? outputTokens, string? finishReason);

    private sealed record CompletionPayload(string? Model, List<Choice>? Choices, Usage? Usage);

    private sealed record Choice(Message? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record Message(string? Content);

    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

    private sealed record StreamPayload(List<StreamChoice>? Choices, JsonElement? Error);

    private sealed record StreamChoice(Message? Delta);
}
