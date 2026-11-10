namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>Port to an embedding model: turns text into vectors whose distance reflects meaning.</summary>
public interface IEmbeddingGenerator
{
    /// <summary>Identifier of the model in use; stored with each vector.</summary>
    string ModelId { get; }

    /// <summary>Generates one vector per input, in the same order.</summary>
    /// <param name="inputs">Texts to embed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One non-empty vector per input, all of the same dimension.</returns>
    /// <exception cref="Exceptions.LlmUnavailableException">The provider is not configured, unreachable, rejected the call or returned unusable vectors.</exception>
    /// <exception cref="Exceptions.LlmTimeoutException">The provider timed out.</exception>
    Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}
