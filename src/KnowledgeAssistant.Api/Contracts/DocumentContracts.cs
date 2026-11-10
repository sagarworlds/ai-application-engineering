using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Domain.Documents;

namespace KnowledgeAssistant.Api.Contracts;

/// <summary>Document metadata returned by the API.</summary>
/// <param name="Id">Document identifier.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="Title">Display title.</param>
/// <param name="PageCount">Number of pages ingested.</param>
/// <param name="ChunkCount">Number of searchable chunks.</param>
/// <param name="AllowedRoles">Roles that may read the document; empty means all tenant members.</param>
/// <param name="UploadedByUserId">User who ingested the document.</param>
/// <param name="CreatedAtUtc">Ingestion time (UTC).</param>
public sealed record DocumentResponse(
    Guid Id,
    string FileName,
    string Title,
    int PageCount,
    int ChunkCount,
    IReadOnlyList<string> AllowedRoles,
    string UploadedByUserId,
    DateTimeOffset CreatedAtUtc);

/// <summary>A page of results.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="TotalCount">Total items across all pages.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal static class DocumentMappings
{
    // The tenant id is deliberately not part of the response: callers only ever see their own tenant.
    public static DocumentResponse ToResponse(this Document document) => new(
        document.Id,
        document.FileName,
        document.Title,
        document.PageCount,
        document.ChunkCount,
        document.AllowedRoles,
        document.UploadedByUserId,
        document.CreatedAtUtc);

    public static PagedResponse<DocumentResponse> ToResponse(this PagedResult<Document> page) => new(
        [.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.TotalCount);
}
