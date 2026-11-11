using System.ComponentModel.DataAnnotations;

using KnowledgeAssistant.Application.Questions;

namespace KnowledgeAssistant.Api.Contracts;

/// <summary>A question about the caller's documents.</summary>
public sealed record QuestionRequest
{
    /// <summary>Longest accepted question, in characters.</summary>
    public const int MaxQuestionLength = 2000;

    /// <summary>The question text.</summary>
    [Required]
    [StringLength(MaxQuestionLength, MinimumLength = 3)]
    public string Question { get; init; } = string.Empty;

    /// <summary>Conversation to continue. Omit to start a new conversation.</summary>
    public Guid? ConversationId { get; init; }
}

/// <summary>A source passage supporting the answer.</summary>
/// <param name="SourceNumber">Number used in the answer text to refer to this source, e.g. <c>[1]</c>.</param>
/// <param name="DocumentId">Document identifier.</param>
/// <param name="ChunkId">Chunk identifier.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="Title">Document title.</param>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="Section">Section heading, when known.</param>
/// <param name="Snippet">Short excerpt of the passage.</param>
public sealed record CitationResponse(
    int SourceNumber,
    Guid DocumentId,
    Guid ChunkId,
    string FileName,
    string Title,
    int PageNumber,
    string? Section,
    string Snippet);

/// <summary>
/// The answer to a question. Either <c>answerFound</c> is true with at least one citation, or it
/// is false with the fixed unknown-answer text and no citations.
/// </summary>
/// <param name="ConversationId">Conversation identifier to send with follow-up questions.</param>
/// <param name="AnswerFound">Whether the documents supported an answer.</param>
/// <param name="Answer">Answer text.</param>
/// <param name="Citations">Supporting sources.</param>
public sealed record QuestionResponse(
    Guid ConversationId,
    bool AnswerFound,
    string Answer,
    IReadOnlyList<CitationResponse> Citations);

internal static class QuestionMappings
{
    public static QuestionResponse ToResponse(this GroundedAnswer answer) => new(
        answer.ConversationId,
        answer.AnswerFound,
        answer.Answer,
        [.. answer.Citations.Select(c => new CitationResponse(
            c.SourceNumber, c.DocumentId, c.ChunkId, c.FileName, c.Title, c.PageNumber, c.Section, c.Snippet))]);
}
