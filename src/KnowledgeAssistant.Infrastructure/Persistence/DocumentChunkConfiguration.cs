using System.Runtime.InteropServices;

using KnowledgeAssistant.Domain.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAssistant.Infrastructure.Persistence;

internal sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).HasMaxLength(PersistenceLimits.IdentifierLength).IsRequired();
        builder.Property(c => c.Section).HasMaxLength(DocumentChunk.MaxSectionLength);
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.EmbeddingModel).HasMaxLength(128).IsRequired();

        builder.Property(c => c.Embedding)
            .HasConversion(vector => VectorBlob.ToBytes(vector), bytes => VectorBlob.ToVector(bytes))
            .Metadata.SetValueComparer(new ValueComparer<float[]>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Length,
                v => v.ToArray()));

        // Deleting a document removes its chunks in the database, so no orphaned text can stay searchable.
        builder.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);

        // Vector search always filters on tenant and embedding model first.
        builder.HasIndex(c => new { c.TenantId, c.EmbeddingModel });
    }
}

/// <summary>Stores a float vector as a compact little-endian BLOB (4 bytes per dimension).</summary>
internal static class VectorBlob
{
    public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    public static float[] ToVector(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}
