using System.Net;
using System.Text;
using System.Text.Json;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Infrastructure.Llm;
using KnowledgeAssistant.UnitTests.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Infrastructure;

public sealed class OpenAiCompatibleChatModelTests
{
    private const string CompletionJson = """
        {
          "model": "gpt-test-2026",
          "choices": [ { "message": { "role": "assistant", "content": "Hello!" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 12, "completion_tokens": 3 }
        }
        """;

    private static readonly ChatCompletionRequest Request = new(
    [
        new ChatMessage(ChatRole.System, "Be brief."),
        new ChatMessage(ChatRole.User, "Hi"),
    ])
    {
        Temperature = 0.2,
        MaxOutputTokens = 50,
    };

    [Fact]
    public async Task CompleteAsync_SendsOpenAiPayloadAndParsesReply()
    {
        var handler = StubHttpMessageHandler.Json(CompletionJson);
        var model = CreateModel(handler);

        var completion = await model.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal("Hello!", completion.Content);
        Assert.Equal("gpt-test-2026", completion.Model);
        Assert.Equal(new TokenUsage(12, 3), completion.Usage);
        Assert.Equal("stop", completion.FinishReason);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://llm.example/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer unit-test-key", request.Headers.Authorization!.ToString());

        using var sent = JsonDocument.Parse(body);
        Assert.Equal("chat-model", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.2, sent.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(50, sent.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(sent.RootElement.TryGetProperty("stream", out _));
        Assert.False(sent.RootElement.TryGetProperty("response_format", out _));
        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_AzureStyleKeyHeaderAndTokenParameter_AreHonoured()
    {
        var handler = StubHttpMessageHandler.Json(CompletionJson);
        var model = CreateModel(handler, o =>
        {
            o.ApiKeyHeaderName = "api-key";
            o.MaxTokensParameterName = "max_completion_tokens";
        });

        await model.CompleteAsync(Request, CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("unit-test-key", Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Contains("\"max_completion_tokens\":50", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StructuredOutputMode.JsonSchema, "json_schema")]
    [InlineData(StructuredOutputMode.JsonObject, "json_object")]
    [InlineData(StructuredOutputMode.None, null)]
    public async Task CompleteAsync_ResponseFormat_FollowsConfiguredMode(StructuredOutputMode mode, string? expectedType)
    {
        var handler = StubHttpMessageHandler.Json(CompletionJson);
        var model = CreateModel(handler, o => o.StructuredOutputMode = mode);
        var request = Request with { ResponseFormat = new JsonSchemaFormat("answer", """{"type":"object"}""") };

        await model.CompleteAsync(request, CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.Requests[0].Body);
        if (expectedType is null)
        {
            Assert.False(sent.RootElement.TryGetProperty("response_format", out _));
            return;
        }

        var format = sent.RootElement.GetProperty("response_format");
        Assert.Equal(expectedType, format.GetProperty("type").GetString());
        if (mode == StructuredOutputMode.JsonSchema)
        {
            Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
            Assert.Equal("object", format.GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task CompleteAsync_ProviderTooSlow_ThrowsLlmTimeoutException()
    {
        var model = CreateModel(StubHttpMessageHandler.Hanging(), o => o.RequestTimeout = TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<LlmTimeoutException>(
            () => model.CompleteAsync(Request, CancellationToken.None));

        Assert.Equal(TimeSpan.FromMilliseconds(100), exception.Timeout);
    }

    [Fact]
    public async Task CompleteAsync_CallerCancels_PropagatesCancellationNotTimeout()
    {
        var model = CreateModel(StubHttpMessageHandler.Hanging());
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.CompleteAsync(Request, caller.Token));

        Assert.IsNotType<LlmTimeoutException>(exception);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CompleteAsync_ProviderError_ThrowsUnavailableWithoutLeakingBody(HttpStatusCode status)
    {
        var model = CreateModel(StubHttpMessageHandler.Json("""{"error":{"message":"secret upstream detail"}}""", status));

        var exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => model.CompleteAsync(Request, CancellationToken.None));

        Assert.DoesNotContain("secret upstream detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_NetworkFailure_ThrowsUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => CreateModel(handler).CompleteAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    public async Task CompleteAsync_UnusableBody_ThrowsUnavailable(string body)
    {
        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => CreateModel(StubHttpMessageHandler.Json(body)).CompleteAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_NotConfigured_ThrowsUnavailableWithoutCallingNetwork()
    {
        var handler = StubHttpMessageHandler.Json(CompletionJson);
        var model = CreateModel(handler, o => o.BaseUrl = string.Empty);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => model.CompleteAsync(Request, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task StreamAsync_YieldsDeltasUntilDone()
    {
        const string sse = """
            data: {"choices":[{"delta":{"role":"assistant"}}]}

            data: {"choices":[{"delta":{"content":"Hel"}}]}

            : keep-alive comment
            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: [DONE]

            data: {"choices":[{"delta":{"content":"ignored after DONE"}}]}
            """;
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        }));

        var deltas = new List<string>();
        await foreach (var delta in CreateModel(handler).StreamAsync(Request, CancellationToken.None))
        {
            deltas.Add(delta);
        }

        Assert.Equal(["Hel", "lo"], deltas);
        Assert.Contains("\"stream\":true", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")] // connection closed, no [DONE]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: {\"error\":{\"message\":\"overloaded\"}}\n\ndata: [DONE]\n\n")]
    public async Task StreamAsync_TruncatedOrFailedStream_ThrowsInsteadOfLookingComplete(string sse)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        }));

        var received = new List<string>();
        await Assert.ThrowsAsync<LlmUnavailableException>(async () =>
        {
            await foreach (var delta in CreateModel(handler).StreamAsync(Request, CancellationToken.None))
            {
                received.Add(delta);
            }
        });

        Assert.Equal(["partial"], received);
    }

    [Fact]
    public async Task StreamAsync_CallerCancels_PropagatesCancellationNotTimeout()
    {
        var model = CreateModel(StubHttpMessageHandler.Hanging());
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in model.StreamAsync(Request, caller.Token))
            {
            }
        });

        Assert.IsNotType<LlmTimeoutException>(exception);
    }

    [Fact]
    public async Task StreamAsync_ProviderStalls_ThrowsLlmTimeoutException()
    {
        var model = CreateModel(StubHttpMessageHandler.Hanging(), o => o.StreamTimeout = TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<LlmTimeoutException>(async () =>
        {
            await foreach (var _ in model.StreamAsync(Request, CancellationToken.None))
            {
            }
        });
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("https://my-resource.openai.azure.com/openai/v1/", true)]
    [InlineData("http://localhost:5099/v1/", true)]
    [InlineData("http://llm.internal.example/v1/", false)] // key and prompts over cleartext
    [InlineData("ftp://llm.example/", false)]
    [InlineData("not a url", false)]
    public void OptionsValidator_AcceptsOnlySafeBaseUrls(string baseUrl, bool expectedValid)
    {
        var result = new LlmOptionsValidator().Validate(null, new LlmOptions { BaseUrl = baseUrl });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    private static OpenAiCompatibleChatModel CreateModel(HttpMessageHandler handler, Action<LlmOptions>? configure = null)
    {
        var options = new LlmOptions
        {
            BaseUrl = "https://llm.example/v1",
            ApiKey = "unit-test-key",
            ChatModel = "chat-model",
        };
        configure?.Invoke(options);

        var client = new HttpClient(handler);
        LlmHttp.Configure(client, options);
        return new OpenAiCompatibleChatModel(client, Options.Create(options), NullLogger<OpenAiCompatibleChatModel>.Instance);
    }
}
