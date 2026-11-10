namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>Who authored a chat message.</summary>
public enum ChatRole
{
    /// <summary>Instructions from the application; highest priority for the model.</summary>
    System,

    /// <summary>Input from the end user (and any data we attach for them).</summary>
    User,

    /// <summary>A previous model reply.</summary>
    Assistant,
}

/// <summary>One message in a chat exchange.</summary>
/// <param name="Role">Author of the message.</param>
/// <param name="Content">Message text.</param>
public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>Asks the model to reply with JSON conforming to a schema (structured output).</summary>
/// <param name="Name">Schema name (letters, digits, underscores).</param>
/// <param name="SchemaJson">The JSON Schema document.</param>
public sealed record JsonSchemaFormat(string Name, string SchemaJson);

/// <summary>A request for a chat completion.</summary>
/// <param name="Messages">Conversation so far, oldest first.</param>
public sealed record ChatCompletionRequest(IReadOnlyList<ChatMessage> Messages)
{
    /// <summary>Sampling temperature; lower is more deterministic. <c>null</c> uses the provider default.</summary>
    public double? Temperature { get; init; }

    /// <summary>Upper bound on generated tokens. <c>null</c> uses the provider default.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>When set, the model must answer with JSON matching this schema.</summary>
    public JsonSchemaFormat? ResponseFormat { get; init; }
}

/// <summary>Token accounting reported by the provider.</summary>
/// <param name="InputTokens">Tokens in the prompt.</param>
/// <param name="OutputTokens">Tokens generated.</param>
public sealed record TokenUsage(int InputTokens, int OutputTokens);

/// <summary>A completed model reply.</summary>
/// <param name="Content">Reply text.</param>
/// <param name="Model">Model that produced the reply, as reported by the provider.</param>
/// <param name="Usage">Token usage, when reported.</param>
/// <param name="FinishReason">Why generation stopped (<c>stop</c>, <c>length</c>, …), when reported.</param>
public sealed record ChatCompletion(string Content, string Model, TokenUsage? Usage, string? FinishReason);

/// <summary>Port to a chat-completion model.</summary>
public interface IChatModel
{
    /// <summary>Generates a complete reply.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the call (for example, when the client disconnects).</param>
    /// <returns>The model's reply.</returns>
    /// <exception cref="Exceptions.LlmUnavailableException">The provider is not configured, unreachable or rejected the call.</exception>
    /// <exception cref="Exceptions.LlmTimeoutException">The provider did not answer within the configured timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ChatCompletion> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken);

    /// <summary>Generates a reply incrementally.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>Text fragments in generation order.</returns>
    /// <exception cref="Exceptions.LlmUnavailableException">The provider is not configured, unreachable or rejected the call.</exception>
    /// <exception cref="Exceptions.LlmTimeoutException">The stream did not finish within the configured timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
}
