using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>A passage retrieved as potential evidence for a question.</summary>
/// <param name="ChunkId">Chunk identifier.</param>
/// <param name="DocumentId">Owning document.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="Title">Document title.</param>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="Section">Section heading, when known.</param>
/// <param name="Content">Passage text.</param>
/// <param name="Score">Relevance to the query; higher is better (cosine similarity for vector search).</param>
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    string Title,
    int PageNumber,
    string? Section,
    string Content,
    double Score);

/// <summary>
/// Finds passages relevant to a query <em>among the documents the caller may read</em>.
/// This port is the security boundary for retrieval: implementations must apply the tenant
/// filter and document ACL themselves, because whatever they return is shown to the model.
/// </summary>
public interface IChunkRetriever
{
    /// <summary>Searches for passages relevant to <paramref name="query"/>.</summary>
    /// <param name="user">The caller; only passages from documents accessible to them may be returned.</param>
    /// <param name="query">Natural-language query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Relevant passages, best first. Empty when nothing is relevant enough.</returns>
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(UserContext user, string query, CancellationToken cancellationToken);
}
