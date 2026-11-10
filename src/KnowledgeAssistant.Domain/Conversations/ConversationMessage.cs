using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Domain.Conversations;

/// <summary>Author of a stored conversation message.</summary>
public enum ConversationRole
{
    /// <summary>The end user's question.</summary>
    User,

    /// <summary>The assistant's answer.</summary>
    Assistant,
}

/// <summary>One stored turn of a conversation, owned by a single user within a tenant.</summary>
public sealed class ConversationMessage
{
    // Required by EF Core materialisation.
    private ConversationMessage()
    {
    }

    /// <summary>Unique identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant of the owning user.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>User who owns the conversation. Conversations are never shared between users.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Conversation the message belongs to.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Position within the conversation; larger is newer.</summary>
    public long Sequence { get; private set; }

    /// <summary>Author of the message.</summary>
    public ConversationRole Role { get; private set; }

    /// <summary>Message text.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Creation time (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Creates a message owned by <paramref name="owner"/>.</summary>
    /// <param name="owner">The user the conversation belongs to.</param>
    /// <param name="conversationId">Conversation identifier.</param>
    /// <param name="sequence">Ordering key; must increase with each message of the conversation.</param>
    /// <param name="role">Author.</param>
    /// <param name="content">Message text.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    /// <returns>The new message.</returns>
    /// <exception cref="ArgumentException"><paramref name="content"/> is blank.</exception>
    public static ConversationMessage Create(
        UserContext owner,
        Guid conversationId,
        long sequence,
        ConversationRole role,
        string content,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        return new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId,
            UserId = owner.UserId,
            ConversationId = conversationId,
            Sequence = sequence,
            Role = role,
            Content = content,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
        };
    }
}
