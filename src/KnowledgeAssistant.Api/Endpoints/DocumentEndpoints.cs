using System.ComponentModel.DataAnnotations;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.RateLimiting;
using KnowledgeAssistant.Application.Documents;
using KnowledgeAssistant.Application.Ingestion;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Endpoints;

internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/documents")
            .WithTags("Documents")
            .RequireAuthorization(AuthorizationPolicies.DocumentsRead)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync)
            .WithName("ListDocuments")
            .WithSummary("Lists metadata of documents the caller may read, newest first.")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetDocument")
            .WithSummary("Gets one document's metadata.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", IngestAsync)
            .WithName("IngestDocument")
            .WithSummary("Ingests extracted document text: chunks it, embeds the chunks and stores them.")
            .RequireAuthorization(AuthorizationPolicies.DocumentsWrite)
            .RequireRateLimiting(ModelRateLimiting.PolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxIngestRequestBytes))
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteDocument")
            .WithSummary("Deletes a document and its chunks.")
            .RequireAuthorization(AuthorizationPolicies.DocumentsWrite)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Generous for the default 2,000,000-character limit (multi-byte text plus JSON escaping),
    // while keeping oversized bodies from being buffered at all.
    private const long MaxIngestRequestBytes = 16 * 1024 * 1024;

    private static async Task<CreatedAtRoute<DocumentResponse>> IngestAsync(
        IngestDocumentRequest request,
        ICurrentUser currentUser,
        DocumentIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        var document = await ingestion.IngestAsync(currentUser.User, request.ToCommand(), cancellationToken);
        return TypedResults.CreatedAtRoute(document.ToResponse(), "GetDocument", new { id = document.Id });
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id, ICurrentUser currentUser, DocumentDeletionService deletion, CancellationToken cancellationToken)
    {
        await deletion.DeleteAsync(currentUser.User, id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<PagedResponse<DocumentResponse>>> ListAsync(
        ICurrentUser currentUser,
        DocumentQueryService documents,
        CancellationToken cancellationToken,
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, DocumentQueryService.MaxPageSize)] int pageSize = 20)
    {
        var result = await documents.ListAsync(currentUser.User, page, pageSize, cancellationToken);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<Ok<DocumentResponse>> GetAsync(
        Guid id, ICurrentUser currentUser, DocumentQueryService documents, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(currentUser.User, id, cancellationToken);
        return TypedResults.Ok(document.ToResponse());
    }
}
