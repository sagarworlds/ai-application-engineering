using System.ComponentModel.DataAnnotations;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Application.Documents;

using Microsoft.AspNetCore.Http.HttpResults;

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

        return app;
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
