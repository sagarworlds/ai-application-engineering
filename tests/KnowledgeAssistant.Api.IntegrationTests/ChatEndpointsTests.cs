using System.Net;
using System.Net.Http.Json;

using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class ChatEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ChatEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.ChatModel.Reset();
    }

    [Fact]
    public async Task Chat_ReturnsReplyModelAndUsage_AndSendsSystemPlusUserMessage()
    {
        _factory.ChatModel.Reply = _ => "A context window is the text a model can attend to at once.";
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "What is a context window?", temperature = 0.3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.NotNull(body);
        Assert.StartsWith("A context window", body.Reply, StringComparison.Ordinal);
        Assert.Equal("fake-model", body.Model);
        Assert.Equal(new UsageResponse(10, 5), body.Usage);

        var sent = Assert.Single(_factory.ChatModel.Requests);
        Assert.Equal([ChatRole.System, ChatRole.User], sent.Messages.Select(m => m.Role));
        Assert.Equal("What is a context window?", sent.Messages[1].Content);
        Assert.Equal(0.3, sent.Temperature);
        Assert.NotNull(sent.MaxOutputTokens);
    }

    [Theory]
    [InlineData("""{"message":""}""")]
    [InlineData("""{"message":"hi","temperature":2.5}""")]
    [InlineData("""{"message":"hi","temperature":-1}""")]
    public async Task Chat_InvalidBody_Returns400_AndNeverCallsTheModel(string json)
    {
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsync(
            "/api/v1/chat", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.ChatModel.Requests);
    }

    [Fact]
    public async Task Chat_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "hi" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Chat_ProviderUnavailable_Returns503Problem()
    {
        _factory.ChatModel.Reply = _ => throw new LlmUnavailableException("The language model provider could not be reached.");
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "hi" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("Language model unavailable", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Chat_ProviderTimeout_Returns504Problem()
    {
        _factory.ChatModel.Reply = _ => throw new LlmTimeoutException(TimeSpan.FromSeconds(30));
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "hi" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task Chat_UnexpectedFailure_Returns500WithoutLeakingExceptionText()
    {
        _factory.ChatModel.Reply = _ => throw new InvalidOperationException("internal detail: connection string xyz");
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "hi" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("internal detail", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStream_EmitsDeltaEventsThenDone()
    {
        _factory.ChatModel.StreamScript = ["Hel", "lo"];
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat/stream", new { message = "hi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal([("delta", "Hel"), ("delta", "lo"), ("done", "")], events);
    }

    [Fact]
    public async Task ChatStream_FailureBeforeFirstFragment_ReturnsProblemInsteadOfAnEmptyStream()
    {
        _factory.ChatModel.StreamScript = [new LlmUnavailableException("The language model is not configured.")];
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat/stream", new { message = "hi" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await response.ReadProblemAsync();
    }

    [Fact]
    public async Task ChatStream_FailureMidStream_EmitsErrorEvent()
    {
        _factory.ChatModel.StreamScript = ["partial", new LlmTimeoutException(TimeSpan.FromSeconds(1))];
        using var client = _factory.CreateClientFor("tenant-a", AppRoles.Reader);

        var response = await client.PostAsJsonAsync("/api/v1/chat/stream", new { message = "hi" });

        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal(("delta", "partial"), events[0]);
        Assert.Equal("error", events[1].Event);
        Assert.Equal(2, events.Count);
    }

    private static List<(string Event, string Data)> ParseEvents(string body) =>
        [.. body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block =>
        {
            var lines = block.Split('\n');
            var name = lines.Single(l => l.StartsWith("event:", StringComparison.Ordinal))[6..].Trim();
            var data = string.Join('\n', lines.Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
            return (name, data);
        })];
}
