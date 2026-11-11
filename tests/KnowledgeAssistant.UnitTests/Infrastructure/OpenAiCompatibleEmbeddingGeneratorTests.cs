using System.Net;
using System.Text.Json;

using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Infrastructure.Llm;
using KnowledgeAssistant.UnitTests.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Infrastructure;

public sealed class OpenAiCompatibleEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_SendsBatchAndReturnsVectorsInInputOrder()
    {
        // Provider answers out of order; "index" must be used to restore input order.
        var handler = StubHttpMessageHandler.Json("""
            { "data": [ { "index": 1, "embedding": [0.3, 0.4] }, { "index": 0, "embedding": [0.1, 0.2] } ] }
            """);

        var vectors = await Create(handler).GenerateAsync(["first", "second"], CancellationToken.None);

        Assert.Equal([[0.1f, 0.2f], [0.3f, 0.4f]], vectors);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://llm.example/v1/embeddings", request.RequestUri!.ToString());
        using var sent = JsonDocument.Parse(body);
        Assert.Equal("embed-model", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal(["first", "second"], sent.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("""{ "data": [ { "index": 0, "embedding": [0.1] } ] }""")] // one vector for two inputs
    [InlineData("""{ "data": [ { "index": 0, "embedding": [0.1] }, { "index": 1, "embedding": [] } ] }""")]
    [InlineData("""{ }""")]
    public async Task GenerateAsync_WrongNumberOfUsableVectors_ThrowsUnavailable(string json)
    {
        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => Create(StubHttpMessageHandler.Json(json)).GenerateAsync(["a", "b"], CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ProviderError_ThrowsUnavailable()
    {
        var handler = StubHttpMessageHandler.Json("{}", HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(handler).GenerateAsync(["a"], CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ProviderTooSlow_ThrowsTimeout()
    {
        var generator = Create(StubHttpMessageHandler.Hanging(), o => o.RequestTimeout = TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<LlmTimeoutException>(() => generator.GenerateAsync(["a"], CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_NotConfigured_ThrowsUnavailable_AndEmptyInputNeedsNoCall()
    {
        var handler = StubHttpMessageHandler.Json("{}");

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => Create(handler, o => o.EmbeddingModel = string.Empty).GenerateAsync(["a"], CancellationToken.None));
        Assert.Empty(await Create(handler).GenerateAsync([], CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static OpenAiCompatibleEmbeddingGenerator Create(HttpMessageHandler handler, Action<LlmOptions>? configure = null)
    {
        var options = new LlmOptions { BaseUrl = "https://llm.example/v1", ApiKey = "unit-test-key", EmbeddingModel = "embed-model" };
        configure?.Invoke(options);

        var client = new HttpClient(handler);
        LlmHttp.Configure(client, options);
        return new OpenAiCompatibleEmbeddingGenerator(
            client, Options.Create(options), NullLogger<OpenAiCompatibleEmbeddingGenerator>.Instance);
    }
}
