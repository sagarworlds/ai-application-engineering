using System.Runtime.CompilerServices;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.UnitTests.TestSupport;

/// <summary>Chat model that replies with queued outputs and records what it was asked.</summary>
internal sealed class ScriptedChatModel(params string[] replies) : IChatModel
{
    private readonly Queue<string> _replies = new(replies);

    public List<ChatCompletionRequest> Requests { get; } = [];

    public Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new ChatCompletion(_replies.Dequeue(), "scripted", null, "stop"));
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class FixedChunkRetriever(params RetrievedChunk[] chunks) : IChunkRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        UserContext user, string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks);
}

internal sealed class InMemoryConversationStore : IConversationStore
{
    private readonly Dictionary<(string Tenant, string User, Guid Conversation), List<ChatMessage>> _messages = [];

    public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(
        UserContext user, Guid conversationId, int maxMessages, CancellationToken cancellationToken)
    {
        var all = _messages.GetValueOrDefault((user.TenantId, user.UserId, conversationId)) ?? [];
        return Task.FromResult<IReadOnlyList<ChatMessage>>([.. all.TakeLast(maxMessages)]);
    }

    public Task AppendExchangeAsync(
        UserContext user, Guid conversationId, string question, string answer, CancellationToken cancellationToken)
    {
        var key = (user.TenantId, user.UserId, conversationId);
        if (!_messages.TryGetValue(key, out var list))
        {
            _messages[key] = list = [];
        }

        list.Add(new ChatMessage(ChatRole.User, question));
        list.Add(new ChatMessage(ChatRole.Assistant, answer));
        return Task.CompletedTask;
    }

    public int TotalMessages => _messages.Values.Sum(l => l.Count);
}

internal static class TestChunks
{
    public static RetrievedChunk Create(string content, string fileName = "travel-policy.pdf", int page = 1, string? section = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), fileName, "Travel Policy", page, section, content, 0.9);
}
