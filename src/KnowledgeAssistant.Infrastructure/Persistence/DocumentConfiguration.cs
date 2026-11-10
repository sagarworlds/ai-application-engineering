using KnowledgeAssistant.Domain.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAssistant.Infrastructure.Persistence;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.TenantId).HasMaxLength(PersistenceLimits.IdentifierLength).IsRequired();
        builder.Property(d => d.UploadedByUserId).HasMaxLength(PersistenceLimits.IdentifierLength).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(Document.MaxNameLength).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(Document.MaxNameLength).IsRequired();
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();

        // SQLite cannot ORDER BY DateTimeOffset; unix milliseconds sort correctly and round-trip as UTC.
        builder.Property(d => d.CreatedAtUtc).HasConversion(
            value => value.ToUnixTimeMilliseconds(),
            stored => DateTimeOffset.FromUnixTimeMilliseconds(stored));

        // The same content may exist in two tenants, but only once per tenant.
        builder.HasIndex(d => new { d.TenantId, d.ContentHash }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.CreatedAtUtc });
    }
}

/// <summary>Column size limits shared by entity configurations.</summary>
internal static class PersistenceLimits
{
    /// <summary>Maximum length of tenant and user identifiers taken from token claims.</summary>
    public const int IdentifierLength = 128;
}
