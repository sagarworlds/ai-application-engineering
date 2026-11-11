using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.Infrastructure.Persistence;
using KnowledgeAssistant.UnitTests.TestSupport;

namespace KnowledgeAssistant.UnitTests.Infrastructure;

public sealed class EfConversationStoreTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _conversation = Guid.NewGuid();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestMessagesOldestFirst()
    {
        var user = TestUsers.Reader();
        for (var i = 1; i <= 3; i++)
        {
            await AppendAsync(user, $"q{i}", $"a{i}");
        }

        var recent = await GetRecentAsync(user, maxMessages: 4);

        Assert.Equal(["q2", "a2", "q3", "a3"], recent.Select(m => m.Content));
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant], recent.Select(m => m.Role));
    }

    [Fact]
    public async Task GetRecentAsync_IsScopedToTenantAndUser()
    {
        var alice = new UserContext("tenant-a", "alice", [AppRoles.Reader]);
        await AppendAsync(alice, "alice question", "alice answer");

        Assert.Empty(await GetRecentAsync(new UserContext("tenant-a", "bob", [AppRoles.Admin]), 10));
        Assert.Empty(await GetRecentAsync(new UserContext("tenant-b", "alice", [AppRoles.Admin]), 10));
        Assert.Equal(2, (await GetRecentAsync(alice, 10)).Count);
    }

    [Fact]
    public async Task AppendExchangeAsync_ClockDoesNotAdvance_ExchangesStillKeepTheirOrder()
    {
        var user = TestUsers.Reader();
        var frozen = new FrozenTimeProvider(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
        for (var i = 1; i <= 3; i++)
        {
            await AppendAsync(user, $"q{i}", $"a{i}", frozen);
        }

        var recent = await GetRecentAsync(user, maxMessages: 10);

        Assert.Equal(["q1", "a1", "q2", "a2", "q3", "a3"], recent.Select(m => m.Content));
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task AppendAsync(UserContext user, string question, string answer, TimeProvider? clock = null)
    {
        await using var db = _database.CreateContext();
        await new EfConversationStore(db, clock ?? TimeProvider.System)
            .AppendExchangeAsync(user, _conversation, question, answer, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(UserContext user, int maxMessages)
    {
        await using var db = _database.CreateContext();
        return await new EfConversationStore(db, TimeProvider.System)
            .GetRecentAsync(user, _conversation, maxMessages, CancellationToken.None);
    }
}
