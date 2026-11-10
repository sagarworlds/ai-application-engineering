using KnowledgeAssistant.Application.Abstractions;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Chat;

/// <summary>Free-form chat with the model (no document grounding).</summary>
public sealed class ChatService(IChatModel chatModel, IOptions<GenerationOptions> options)
{
    internal const string SystemPrompt =
        "You are a concise, helpful assistant for employees. " +
        "You have no access to company documents in this conversation; " +
        "if asked about internal policies or data, say you cannot see them here.";

    private readonly GenerationOptions _options = options.Value;

    /// <summary>Gets a complete reply to <paramref name="message"/>.</summary>
    /// <param name="message">The user's message.</param>
    /// <param name="temperature">Optional temperature override.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The model's reply.</returns>
    /// <exception cref="Exceptions.LlmUnavailableException">The provider is unavailable.</exception>
    /// <exception cref="Exceptions.LlmTimeoutException">The provider timed out.</exception>
    public Task<ChatCompletion> CompleteAsync(string message, double? temperature, CancellationToken cancellationToken) =>
        chatModel.CompleteAsync(BuildRequest(message, temperature), cancellationToken);

    /// <summary>Streams a reply to <paramref name="message"/> as text fragments.</summary>
    /// <param name="message">The user's message.</param>
    /// <param name="temperature">Optional temperature override.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>Text fragments in generation order.</returns>
    public IAsyncEnumerable<string> StreamAsync(string message, double? temperature, CancellationToken cancellationToken) =>
        chatModel.StreamAsync(BuildRequest(message, temperature), cancellationToken);

    private ChatCompletionRequest BuildRequest(string message, double? temperature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ChatCompletionRequest(
        [
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, message),
        ])
        {
            Temperature = temperature ?? _options.ChatTemperature,
            MaxOutputTokens = _options.MaxOutputTokens,
        };
    }
}
