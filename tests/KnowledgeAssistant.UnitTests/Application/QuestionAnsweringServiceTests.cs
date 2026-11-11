using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Chat;
using KnowledgeAssistant.Application.Conversations;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Application.Questions;
using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.UnitTests.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Application;

public sealed class QuestionAnsweringServiceTests
{
    private const string ValidAnswer = """{"answerFound": true, "answer": "Book 14 days ahead [1].", "citations": [1]}""";
    private static readonly UserContext User = TestUsers.Reader();
    private readonly InMemoryConversationStore _store = new();

    [Fact]
    public async Task AskAsync_NoSourcesRetrieved_ReturnsUnknownWithoutCallingTheModel()
    {
        var model = new ScriptedChatModel();
        var service = CreateService(model, new FixedChunkRetriever());

        var answer = await service.AskAsync(User, "What is the meaning of life?", null, CancellationToken.None);

        Assert.False(answer.AnswerFound);
        Assert.Equal(GroundedAnswer.UnknownAnswerText, answer.Answer);
        Assert.Empty(answer.Citations);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task AskAsync_ModelAnswersWithCitation_ReturnsAnswerMappedToTheRetrievedChunk()
    {
        var chunk = TestChunks.Create("Flights must be booked at least 14 days in advance.", page: 3, section: "Booking");
        var service = CreateService(new ScriptedChatModel(ValidAnswer), new FixedChunkRetriever(chunk, TestChunks.Create("Other")));

        var answer = await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);

        Assert.True(answer.AnswerFound);
        Assert.Equal("Book 14 days ahead [1].", answer.Answer);
        var citation = Assert.Single(answer.Citations);
        Assert.Equal(1, citation.SourceNumber);
        Assert.Equal(chunk.ChunkId, citation.ChunkId);
        Assert.Equal(chunk.DocumentId, citation.DocumentId);
        Assert.Equal(3, citation.PageNumber);
        Assert.Equal("Booking", citation.Section);
        Assert.Equal(chunk.Content, citation.Snippet);
    }

    [Fact]
    public async Task AskAsync_ModelSaysNotFound_ReturnsFixedUnknownText()
    {
        var model = new ScriptedChatModel("""{"answerFound": false, "answer": "Maybe 30 days?", "citations": []}""");
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Unrelated text.")));

        var answer = await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);

        Assert.False(answer.AnswerFound);
        Assert.Equal(GroundedAnswer.UnknownAnswerText, answer.Answer);
    }

    [Theory]
    [InlineData("""{"answerFound": true, "answer": "It is 30 days.", "citations": []}""")]
    [InlineData("""{"answerFound": true, "answer": "It is 30 days [5].", "citations": [5]}""")]
    public async Task AskAsync_AnswerWithoutAnyValidCitation_IsDowngradedToUnknown(string modelOutput)
    {
        var service = CreateService(new ScriptedChatModel(modelOutput), new FixedChunkRetriever(TestChunks.Create("Some text.")));

        var answer = await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);

        Assert.False(answer.AnswerFound);
        Assert.Empty(answer.Citations);
    }

    [Fact]
    public async Task AskAsync_InvalidOutputOnce_RetriesAndSucceeds()
    {
        var model = new ScriptedChatModel("Sure! The answer is 14 days.", ValidAnswer);
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")));

        var answer = await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);

        Assert.True(answer.AnswerFound);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task AskAsync_InvalidOutputTwice_ThrowsAndStoresNothing()
    {
        var model = new ScriptedChatModel("nope", "still nope");
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")));

        await Assert.ThrowsAsync<LlmOutputInvalidException>(
            () => service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None));

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(0, _store.TotalMessages);
    }

    [Fact]
    public async Task AskAsync_FollowUp_SendsEarlierTurnsAndKeepsConversationId()
    {
        var model = new ScriptedChatModel(ValidAnswer, ValidAnswer);
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")));

        var first = await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);
        var second = await service.AskAsync(User, "And trains?", first.ConversationId, CancellationToken.None);

        Assert.Equal(first.ConversationId, second.ConversationId);
        var followUp = model.Requests[1].Messages;
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], followUp.Select(m => m.Role));
        Assert.Equal("How early must I book flights?", followUp[1].Content);
        Assert.Equal("Book 14 days ahead [1].", followUp[2].Content);
        Assert.Contains("And trains?", followUp[3].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskAsync_LongConversation_HistoryInPromptStaysWithinConfiguredBounds()
    {
        const int maxMessages = 4;
        const int maxCharacters = 200;
        var replies = Enumerable.Repeat(ValidAnswer, 12).ToArray();
        var model = new ScriptedChatModel(replies);
        var service = CreateService(
            model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")),
            new ConversationOptions { MaxHistoryMessages = maxMessages, MaxHistoryCharacters = maxCharacters });

        Guid? conversation = null;
        for (var i = 0; i < replies.Length; i++)
        {
            conversation = (await service.AskAsync(User, $"Question number {i}?", conversation, CancellationToken.None)).ConversationId;
        }

        Assert.All(model.Requests, request =>
        {
            // Everything between the system instruction and the final user message is history.
            var history = request.Messages.Skip(1).SkipLast(1).ToList();
            Assert.True(history.Count <= maxMessages);
            Assert.True(history.Sum(m => m.Content.Length) <= maxCharacters);
        });
        Assert.Equal(maxMessages, model.Requests[^1].Messages.Count - 2);
    }

    [Fact]
    public async Task AskAsync_AnotherUsersConversationId_DoesNotExposeTheirHistory()
    {
        var model = new ScriptedChatModel(ValidAnswer, ValidAnswer);
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")));
        var alice = new UserContext("tenant-a", "alice", [AppRoles.Reader]);
        var bob = new UserContext("tenant-a", "bob", [AppRoles.Reader]);

        var alices = await service.AskAsync(alice, "Alice's private question?", null, CancellationToken.None);
        await service.AskAsync(bob, "Bob guesses the id?", alices.ConversationId, CancellationToken.None);

        Assert.DoesNotContain(model.Requests[1].Messages, m => m.Content.Contains("Alice's private question", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskAsync_UsesLowTemperatureAndStructuredOutput()
    {
        var model = new ScriptedChatModel(ValidAnswer);
        var service = CreateService(model, new FixedChunkRetriever(TestChunks.Create("Book 14 days ahead.")));

        await service.AskAsync(User, "How early must I book flights?", null, CancellationToken.None);

        var request = Assert.Single(model.Requests);
        Assert.Equal(0.1, request.Temperature);
        Assert.NotNull(request.ResponseFormat);
    }

    private QuestionAnsweringService CreateService(
        IChatModel model, IChunkRetriever retriever, ConversationOptions? conversationOptions = null) =>
        new(
            retriever,
            model,
            _store,
            Options.Create(new GenerationOptions()),
            Options.Create(conversationOptions ?? new ConversationOptions()),
            NullLogger<QuestionAnsweringService>.Instance);
}
