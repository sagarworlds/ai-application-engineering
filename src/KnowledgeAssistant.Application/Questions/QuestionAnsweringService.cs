using System.Text.RegularExpressions;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Chat;
using KnowledgeAssistant.Application.Conversations;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Security;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Questions;

/// <summary>
/// Answers a question from the caller's documents: retrieve permitted passages, ask the model
/// for a grounded structured answer, validate it, and return either a cited answer or the fixed
/// unknown answer.
/// </summary>
public sealed partial class QuestionAnsweringService(
    IChunkRetriever retriever,
    IChatModel chatModel,
    IConversationStore conversations,
    IOptions<GenerationOptions> generationOptions,
    IOptions<ConversationOptions> conversationOptions,
    ILogger<QuestionAnsweringService> logger) : IQuestionAnswerer
{
    // One retry: structured-output failures are usually transient sampling noise. More retries
    // would multiply cost and latency for a provider that is misbehaving consistently.
    private const int MaxModelAttempts = 2;
    private const int SnippetLength = 240;

    private readonly GenerationOptions _generation = generationOptions.Value;
    private readonly ConversationOptions _conversation = conversationOptions.Value;

    /// <inheritdoc />
    /// <exception cref="LlmOutputInvalidException">The model kept returning output that fails validation.</exception>
    /// <exception cref="LlmUnavailableException">The model provider is unavailable.</exception>
    /// <exception cref="LlmTimeoutException">The model provider timed out.</exception>
    public async Task<GroundedAnswer> AskAsync(
        UserContext user, string question, Guid? conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var conversation = conversationId ?? Guid.NewGuid();

        // The retriever only returns passages this caller may read; that is the access control.
        var sources = await retriever.SearchAsync(user, question, cancellationToken).ConfigureAwait(false);

        GroundedAnswer answer;
        if (sources.Count == 0)
        {
            // No evidence, no model call: the model cannot be tempted to answer from its own knowledge.
            answer = GroundedAnswer.Unknown(conversation);
        }
        else
        {
            var history = await LoadBoundedHistoryAsync(user, conversation, cancellationToken).ConfigureAwait(false);
            var request = GroundedPromptBuilder.Build(
                question, sources, history, _generation.AnswerTemperature, _generation.MaxOutputTokens);
            var parsed = await CompleteAndValidateAsync(request, sources.Count, cancellationToken).ConfigureAwait(false);
            answer = ToGroundedAnswer(conversation, parsed, sources);
        }

        await conversations
            .AppendExchangeAsync(user, conversation, question, answer.Answer, cancellationToken)
            .ConfigureAwait(false);

        LogAnswered(logger, conversation, sources.Count, answer.AnswerFound, answer.Citations.Count);
        return answer;
    }

    private async Task<IReadOnlyList<ChatMessage>> LoadBoundedHistoryAsync(
        UserContext user, Guid conversationId, CancellationToken cancellationToken)
    {
        if (_conversation.MaxHistoryMessages == 0)
        {
            return [];
        }

        var recent = await conversations
            .GetRecentAsync(user, conversationId, _conversation.MaxHistoryMessages, cancellationToken)
            .ConfigureAwait(false);
        return BoundedHistory.Select(recent, _conversation.MaxHistoryMessages, _conversation.MaxHistoryCharacters);
    }

    private async Task<ParsedAnswer> CompleteAndValidateAsync(
        ChatCompletionRequest request, int sourceCount, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var completion = await chatModel.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                return GroundedAnswerParser.Parse(completion.Content, sourceCount);
            }
            catch (LlmOutputInvalidException exception) when (attempt < MaxModelAttempts)
            {
                LogInvalidOutputRetry(logger, exception.Message, attempt, completion.FinishReason);
            }
        }
    }

    private GroundedAnswer ToGroundedAnswer(Guid conversationId, ParsedAnswer parsed, IReadOnlyList<RetrievedChunk> sources)
    {
        if (!parsed.AnswerFound)
        {
            return GroundedAnswer.Unknown(conversationId);
        }

        if (parsed.Citations.Count == 0)
        {
            // The model claims an answer but cited nothing we supplied. An uncited answer cannot be
            // verified by the user, so it is treated as no answer.
            LogUncitedAnswerDowngraded(logger, conversationId);
            return GroundedAnswer.Unknown(conversationId);
        }

        var citations = parsed.Citations.Select(number =>
        {
            var source = sources[number - 1];
            return new Citation(
                number, source.DocumentId, source.ChunkId, source.FileName, source.Title,
                source.PageNumber, source.Section, ToSnippet(source.Content));
        }).ToList();

        return GroundedAnswer.Answered(conversationId, parsed.Answer, citations);
    }

    private static string ToSnippet(string content)
    {
        var singleLine = Whitespace().Replace(content, " ").Trim();
        return singleLine.Length <= SnippetLength ? singleLine : string.Concat(singleLine.AsSpan(0, SnippetLength), "…");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Question answered in conversation {ConversationId}: {SourceCount} sources retrieved, answerFound={AnswerFound}, {CitationCount} citations")]
    private static partial void LogAnswered(
        ILogger logger, Guid conversationId, int sourceCount, bool answerFound, int citationCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Model output failed validation ({Reason}) on attempt {Attempt} (finish reason {FinishReason}); retrying")]
    private static partial void LogInvalidOutputRetry(ILogger logger, string reason, int attempt, string? finishReason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Model answered without any valid citation in conversation {ConversationId}; returning the unknown answer")]
    private static partial void LogUncitedAnswerDowngraded(ILogger logger, Guid conversationId);
}
