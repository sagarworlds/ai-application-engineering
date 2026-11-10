using KnowledgeAssistant.Domain.Conversations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAssistant.Infrastructure.Persistence;

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId).HasMaxLength(PersistenceLimits.IdentifierLength).IsRequired();
        builder.Property(m => m.UserId).HasMaxLength(PersistenceLimits.IdentifierLength).IsRequired();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.CreatedAtUtc).HasConversion(
            value => value.ToUnixTimeMilliseconds(),
            stored => DateTimeOffset.FromUnixTimeMilliseconds(stored));

        // Matches the only read path: one user's conversation, newest first.
        builder.HasIndex(m => new { m.TenantId, m.UserId, m.ConversationId, m.Sequence });
    }
}
