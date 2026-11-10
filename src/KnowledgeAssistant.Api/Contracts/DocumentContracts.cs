using System.ComponentModel.DataAnnotations;

using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Ingestion;
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

/// <summary>Extracted text of one page.</summary>
public sealed record PageRequest
{
    /// <summary>1-based page number.</summary>
    [Range(1, 100_000)]
    public int PageNumber { get; init; }

    /// <summary>Extracted text of the page. May be empty for blank pages.</summary>
    [Required(AllowEmptyStrings = true)]
    public string Text { get; init; } = string.Empty;
}

/// <summary>Already-extracted document content to ingest (PDF text per page, or a text file as page 1).</summary>
public sealed record IngestDocumentRequest
{
    /// <summary>Hard upper bound on pages per request; the configured ingestion limit may be lower.</summary>
    public const int MaxPagesPerRequest = 10_000;

    /// <summary>Original file name; shown in citations.</summary>
    [Required]
    [StringLength(Document.MaxNameLength, MinimumLength = 1)]
    public string FileName { get; init; } = string.Empty;

    /// <summary>Display title. Defaults to the file name.</summary>
    [StringLength(Document.MaxNameLength)]
    public string? Title { get; init; }

    /// <summary>Roles allowed to read the document. Omit or leave empty for all members of the tenant.</summary>
    public IReadOnlyList<string>? AllowedRoles { get; init; }

    /// <summary>Pages of extracted text.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(MaxPagesPerRequest)]
    public IReadOnlyList<PageRequest> Pages { get; init; } = [];

    // There is intentionally no tenant or uploader field: both come from the caller's token.
}

/// <summary>A page of results.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="TotalCount">Total items across all pages.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal static class DocumentMappings
{
    public static IngestDocumentCommand ToCommand(this IngestDocumentRequest request) => new(
        request.FileName,
        request.Title,
        [.. request.Pages.Select(p => new PageContent(p.PageNumber, p.Text))],
        request.AllowedRoles);

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
