using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Conversations;

namespace KnowledgeAssistant.UnitTests.Application;

public sealed class BoundedHistoryTests
{
    private static readonly ChatMessage[] Conversation =
    [
        new(ChatRole.User, "q1"), new(ChatRole.Assistant, "a1"),
        new(ChatRole.User, "q2"), new(ChatRole.Assistant, "a2"),
        new(ChatRole.User, "q3"), new(ChatRole.Assistant, "a3"),
    ];

    [Fact]
    public void Select_KeepsNewestMessagesWithinTheCountLimit()
    {
        var kept = BoundedHistory.Select(Conversation, maxMessages: 4, maxCharacters: 1000);

        Assert.Equal(["q2", "a2", "q3", "a3"], kept.Select(m => m.Content));
    }

    [Fact]
    public void Select_StopsAtTheCharacterBudget_WithoutSplittingMessages()
    {
        ChatMessage[] history =
        [
            new(ChatRole.User, new string('a', 50)), new(ChatRole.Assistant, new string('b', 50)),
            new(ChatRole.User, new string('c', 30)), new(ChatRole.Assistant, new string('d', 30)),
        ];

        var kept = BoundedHistory.Select(history, maxMessages: 10, maxCharacters: 100);

        Assert.Equal(2, kept.Count);
        Assert.All(kept, m => Assert.Equal(30, m.Content.Length));
    }

    [Fact]
    public void Select_DropsALeadingAnswerWhoseQuestionWasCutOff()
    {
        var kept = BoundedHistory.Select(Conversation, maxMessages: 3, maxCharacters: 1000);

        Assert.Equal(["q3", "a3"], kept.Select(m => m.Content));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(10, 0)]
    [InlineData(1, 1000)] // only "a3" fits, and an answer alone is dropped
    public void Select_NothingFits_ReturnsEmpty(int maxMessages, int maxCharacters)
    {
        Assert.Empty(BoundedHistory.Select(Conversation, maxMessages, maxCharacters));
    }
}
