namespace KnowledgeAssistant.Application.Exceptions;

/// <summary>The model provider is not configured, cannot be reached, or rejected the request.</summary>
public sealed class LlmUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Client-safe description (no upstream response text).</param>
    /// <param name="innerException">Underlying failure, for logs.</param>
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The model provider did not respond within the configured timeout.</summary>
public sealed class LlmTimeoutException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="timeout">The timeout that elapsed.</param>
    /// <param name="innerException">Underlying cancellation, for logs.</param>
    public LlmTimeoutException(TimeSpan timeout, Exception? innerException = null)
        : base($"The language model did not respond within {timeout.TotalSeconds:0.#} seconds.", innerException)
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}
