using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Conversations;
using KnowledgeAssistant.Domain.Security;

using Microsoft.EntityFrameworkCore;

namespace KnowledgeAssistant.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IConversationStore"/>.</summary>
internal sealed class EfConversationStore(KnowledgeDbContext db, TimeProvider timeProvider) : IConversationStore
{
    private static long s_lastSequence;

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(
        UserContext user, Guid conversationId, int maxMessages, CancellationToken cancellationToken)
    {
        // Filtering on tenant AND user means a guessed conversation id from someone else yields nothing.
        var newestFirst = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == user.TenantId && m.UserId == user.UserId && m.ConversationId == conversationId)
            .OrderByDescending(m => m.Sequence)
            .Take(maxMessages)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        newestFirst.Reverse();
        return [.. newestFirst.Select(m => new ChatMessage(
            m.Role == ConversationRole.User ? ChatRole.User : ChatRole.Assistant, m.Content))];
    }

    public async Task AppendExchangeAsync(
        UserContext user, Guid conversationId, string question, string answer, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var sequence = NextSequence(now.UtcTicks);
        db.ConversationMessages.AddRange(
            ConversationMessage.Create(user, conversationId, sequence, ConversationRole.User, question, now),
            ConversationMessage.Create(user, conversationId, sequence + 1, ConversationRole.Assistant, answer, now));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns an ordering key for a question; the answer uses the key plus one. Keys follow the
    /// clock but never repeat or go backwards within this process, so two exchanges saved in the
    /// same clock tick (parallel requests, a coarse or fixed clock) cannot interleave.
    /// </summary>
    private static long NextSequence(long utcTicks)
    {
        while (true)
        {
            var last = Interlocked.Read(ref s_lastSequence);
            // +2 leaves room for the previous exchange's answer.
            var next = Math.Max(utcTicks, last + 2);
            if (Interlocked.CompareExchange(ref s_lastSequence, next, last) == last)
            {
                return next;
            }
        }
    }
}
