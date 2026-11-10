using System.Runtime.CompilerServices;

using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

/// <summary>Scriptable <see cref="IChatModel"/>: records requests and replies as the test dictates.</summary>
public sealed class FakeChatModel : IChatModel
{
    private readonly Lock _gate = new();
    private readonly List<ChatCompletionRequest> _requests = [];

    /// <summary>Produces the reply text for a request. Throw to simulate provider failures.</summary>
    public Func<ChatCompletionRequest, string> Reply { get; set; } = _ => "fake reply";

    /// <summary>Fragments for streaming; an exception entry is thrown at that position.</summary>
    public IReadOnlyList<object> StreamScript { get; set; } = ["fake ", "stream"];

    public IReadOnlyList<ChatCompletionRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
        }

        Reply = _ => "fake reply";
        StreamScript = ["fake ", "stream"];
    }

    public Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        Record(request);
        return Task.FromResult(new ChatCompletion(Reply(request), "fake-model", new TokenUsage(10, 5), "stop"));
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Record(request);
        foreach (var step in StreamScript)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return step is Exception failure ? throw failure : (string)step;
        }
    }

    private void Record(ChatCompletionRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }
    }
}
