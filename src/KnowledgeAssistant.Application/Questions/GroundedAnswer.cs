namespace KnowledgeAssistant.Application.Questions;

/// <summary>A source passage that supports an answer.</summary>
/// <param name="SourceNumber">The number the answer text uses to refer to this source, e.g. <c>[1]</c>.</param>
/// <param name="DocumentId">Document the passage belongs to.</param>
/// <param name="ChunkId">Stored chunk the passage came from.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="Title">Document title.</param>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="Section">Section heading, when one was detected.</param>
/// <param name="Snippet">Short excerpt of the passage.</param>
public sealed record Citation(
    int SourceNumber,
    Guid DocumentId,
    Guid ChunkId,
    string FileName,
    string Title,
    int PageNumber,
    string? Section,
    string Snippet);

/// <summary>
/// The outcome of a question. It has exactly two shapes: answered with at least one citation,
/// or unknown with none. Use the factory methods to keep that invariant.
/// </summary>
public sealed record GroundedAnswer
{
    /// <summary>Fixed text returned whenever the documents do not support an answer.</summary>
    public const string UnknownAnswerText =
        "I couldn't find enough information in the available documents to answer that.";

    private GroundedAnswer(Guid conversationId, bool answerFound, string answer, IReadOnlyList<Citation> citations)
    {
        ConversationId = conversationId;
        AnswerFound = answerFound;
        Answer = answer;
        Citations = citations;
    }

    /// <summary>Conversation this exchange belongs to.</summary>
    public Guid ConversationId { get; }

    /// <summary>True when the answer is supported by the cited sources.</summary>
    public bool AnswerFound { get; }

    /// <summary>The answer text, or <see cref="UnknownAnswerText"/>.</summary>
    public string Answer { get; }

    /// <summary>Supporting sources; empty exactly when <see cref="AnswerFound"/> is false.</summary>
    public IReadOnlyList<Citation> Citations { get; }

    /// <summary>Creates an answered result.</summary>
    /// <exception cref="ArgumentException">The answer is blank or has no citations.</exception>
    public static GroundedAnswer Answered(Guid conversationId, string answer, IReadOnlyList<Citation> citations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(citations);
        if (citations.Count == 0)
        {
            throw new ArgumentException("An answered result requires at least one citation.", nameof(citations));
        }

        return new GroundedAnswer(conversationId, true, answer, citations);
    }

    /// <summary>Creates the safe "insufficient evidence" result.</summary>
    public static GroundedAnswer Unknown(Guid conversationId) =>
        new(conversationId, false, UnknownAnswerText, []);
}
