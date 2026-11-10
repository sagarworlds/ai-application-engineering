using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Questions;
using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// Development vector search: embeds the query, then scores every chunk the caller may read with
/// cosine similarity (exhaustive scan). Correct but O(n) — a search service or vector index
/// replaces this class in production without touching its callers.
/// </summary>
internal sealed class VectorChunkRetriever(
    KnowledgeDbContext db,
    IEmbeddingGenerator embeddings,
    IOptions<RetrievalOptions> options) : IChunkRetriever
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        UserContext user, string query, CancellationToken cancellationToken)
    {
        var queryVector = (await embeddings.GenerateAsync([query], cancellationToken).ConfigureAwait(false))[0];
        var model = embeddings.ModelId;

        // Security boundary of retrieval: only chunks of documents that pass the same tenant + ACL
        // rule as metadata reads are candidates. The chunk's own tenant column is checked as well,
        // so a mistake in the join could still not cross tenants.
        var candidates = db.Chunks.AsNoTracking()
            .Where(c => c.TenantId == user.TenantId && c.EmbeddingModel == model)
            .Join(db.Documents.WhereAccessibleTo(user), c => c.DocumentId, d => d.Id, (c, _) => new { c.Id, c.Embedding })
            .AsAsyncEnumerable();

        // Keep only the best TopK while streaming: a min-heap whose root is the weakest kept hit.
        var best = new PriorityQueue<Guid, double>();
        await foreach (var candidate in candidates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (VectorMath.CosineSimilarity(queryVector, candidate.Embedding) is not { } score || score < _options.MinScore)
            {
                continue;
            }

            best.Enqueue(candidate.Id, score);
            if (best.Count > _options.TopK)
            {
                best.Dequeue();
            }
        }

        if (best.Count == 0)
        {
            return [];
        }

        var scores = best.UnorderedItems.ToDictionary(item => item.Element, item => item.Priority);
        var ids = scores.Keys.ToList();

        // Second, small query for the text of the winners only (avoids loading every chunk's content).
        var hits = await db.Chunks.AsNoTracking()
            .Where(c => c.TenantId == user.TenantId && ids.Contains(c.Id))
            .Join(db.Documents.WhereAccessibleTo(user), c => c.DocumentId, d => d.Id,
                (c, d) => new { c.Id, c.DocumentId, d.FileName, d.Title, c.PageNumber, c.Section, c.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. hits
            .Select(h => new RetrievedChunk(h.Id, h.DocumentId, h.FileName, h.Title, h.PageNumber, h.Section, h.Content, scores[h.Id]))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.ChunkId)];
    }
}
