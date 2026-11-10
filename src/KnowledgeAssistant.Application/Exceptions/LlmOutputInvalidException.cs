namespace KnowledgeAssistant.Application.Exceptions;

/// <summary>The model replied, but not in the structured format the application requires.</summary>
public sealed class LlmOutputInvalidException(string reason)
    : Exception($"The language model returned an invalid response: {reason}");
