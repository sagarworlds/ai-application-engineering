using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Infrastructure.Persistence;

/// <summary>
/// Placeholder until ingestion and vector search exist: finds nothing, so every question
/// receives the safe unknown answer.
/// </summary>
internal sealed class EmptyChunkRetriever : IChunkRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        UserContext user, string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);
}
