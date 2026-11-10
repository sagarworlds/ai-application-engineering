using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Application.Conversations;

/// <summary>Limits on conversation history sent to the model (configuration section <c>Conversation</c>).</summary>
public sealed class ConversationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Conversation";

    /// <summary>Maximum number of earlier messages included in a prompt. 0 disables history.</summary>
    [Range(0, 50)]
    public int MaxHistoryMessages { get; set; } = 6;

    /// <summary>
    /// Character budget for the included history (~4 characters per token). Keeps prompt size, cost
    /// and latency bounded no matter how long a conversation runs.
    /// </summary>
    [Range(0, 100_000)]
    public int MaxHistoryCharacters { get; set; } = 6000;
}
