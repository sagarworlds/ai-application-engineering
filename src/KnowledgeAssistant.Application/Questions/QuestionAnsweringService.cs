using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Questions;

/// <summary>
/// Answers questions from the caller's documents. Retrieval and the model are wired in on later
/// days; until then every question safely yields the unknown answer rather than an invented one.
/// </summary>
public sealed class QuestionAnsweringService : IQuestionAnswerer
{
    /// <inheritdoc />
    public Task<GroundedAnswer> AskAsync(
        UserContext user, string question, Guid? conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GroundedAnswer.Unknown(conversationId ?? Guid.NewGuid()));
    }
}
