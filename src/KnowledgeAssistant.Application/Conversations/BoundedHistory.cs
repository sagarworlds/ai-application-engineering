using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Application.Conversations;

/// <summary>Selects the slice of conversation history that fits the configured limits.</summary>
internal static class BoundedHistory
{
    /// <summary>
    /// Keeps the newest messages that fit within both limits. Messages are never cut in half:
    /// a truncated answer could change its meaning.
    /// </summary>
    /// <param name="messages">Full history, oldest first.</param>
    /// <param name="maxMessages">Maximum number of messages to keep.</param>
    /// <param name="maxCharacters">Maximum total characters to keep.</param>
    /// <returns>The retained messages, oldest first, starting with a user message.</returns>
    public static IReadOnlyList<ChatMessage> Select(IReadOnlyList<ChatMessage> messages, int maxMessages, int maxCharacters)
    {
        var kept = new List<ChatMessage>();
        var characters = 0;

        // Walk backwards from the newest message: recent context matters most.
        for (var i = messages.Count - 1; i >= 0 && kept.Count < maxMessages; i--)
        {
            characters += messages[i].Content.Length;
            if (characters > maxCharacters)
            {
                break;
            }

            kept.Add(messages[i]);
        }

        kept.Reverse();

        // An answer without the question it responded to is confusing context; drop orphans at the start.
        var firstUser = kept.FindIndex(m => m.Role == ChatRole.User);
        return firstUser < 0 ? [] : kept[firstUser..];
    }
}
