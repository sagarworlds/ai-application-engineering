using KnowledgeAssistant.Application.Exceptions;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.ErrorHandling;

/// <summary>
/// Turns every unhandled exception into an RFC 9457 problem response, so clients see one error
/// shape regardless of where a failure originated.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    // Non-standard but widely used (nginx) status for "client closed request"; only visible in logs.
    private const int StatusClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller went away; there is nobody to answer and nothing went wrong.
            LogRequestCancelled(logger, httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusClientClosedRequest;
            return true;
        }

        var problem = ExceptionProblemMapper.Map(exception);
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogServerError(logger, exception, status, httpContext.Request.Path);
        }
        else
        {
            LogClientError(logger, status, exception.GetType().Name, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request to {Path} was cancelled by the client")]
    private static partial void LogRequestCancelled(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request to {Path} failed with status {StatusCode}")]
    private static partial void LogServerError(ILogger logger, Exception exception, int statusCode, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request to {Path} rejected with status {StatusCode} ({ExceptionType})")]
    private static partial void LogClientError(ILogger logger, int statusCode, string exceptionType, string path);
}

/// <summary>Maps exception types to problem details. The only place status codes are decided for failures.</summary>
internal static class ExceptionProblemMapper
{
    public static ProblemDetails Map(Exception exception) => exception switch
    {
        RequestValidationException validation => new HttpValidationProblemDetails(
            validation.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
        },
        NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message),
        ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),
        // Malformed JSON, wrong content type, unparsable route values.
        BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Bad request", "The request could not be read."),

        // Unknown failures: never echo exception text to the client, it may contain internals.
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred", null),
    };

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
