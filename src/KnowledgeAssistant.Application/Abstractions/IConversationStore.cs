using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>Stores conversation turns per user. A conversation is visible only to the user who created it.</summary>
public interface IConversationStore
{
    /// <summary>Gets the most recent messages of a conversation.</summary>
    /// <param name="user">Owner of the conversation.</param>
    /// <param name="conversationId">Conversation identifier.</param>
    /// <param name="maxMessages">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Up to <paramref name="maxMessages"/> messages, oldest first. Empty for unknown or foreign conversations.</returns>
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(
        UserContext user, Guid conversationId, int maxMessages, CancellationToken cancellationToken);

    /// <summary>Appends one question/answer exchange.</summary>
    /// <param name="user">Owner of the conversation.</param>
    /// <param name="conversationId">Conversation identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="answer">The assistant's answer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task AppendExchangeAsync(
        UserContext user, Guid conversationId, string question, string answer, CancellationToken cancellationToken);
}
