using KnowledgeAssistant.Application.Questions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>Answers a question using only documents the caller may read.</summary>
public interface IQuestionAnswerer
{
    /// <summary>Answers <paramref name="question"/> for <paramref name="user"/>.</summary>
    /// <param name="user">The caller; determines which documents can be used.</param>
    /// <param name="question">The question text.</param>
    /// <param name="conversationId">Existing conversation to continue, or <c>null</c> to start one.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A cited answer, or the unknown answer when evidence is insufficient.</returns>
    Task<GroundedAnswer> AskAsync(
        UserContext user, string question, Guid? conversationId, CancellationToken cancellationToken);
}
