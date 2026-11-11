using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Api.Contracts;

/// <summary>A free-form chat message (not grounded in documents).</summary>
public sealed record ChatRequest
{
    /// <summary>Longest accepted message, in characters.</summary>
    public const int MaxMessageLength = 8000;

    /// <summary>The user's message.</summary>
    [Required]
    [StringLength(MaxMessageLength, MinimumLength = 1)]
    public string Message { get; init; } = string.Empty;

    /// <summary>Sampling temperature, 0 (deterministic) to 2 (most varied). Omit for the server default.</summary>
    [Range(0.0, 2.0)]
    public double? Temperature { get; init; }
}

/// <summary>Token usage for a model call.</summary>
/// <param name="InputTokens">Prompt tokens.</param>
/// <param name="OutputTokens">Generated tokens.</param>
public sealed record UsageResponse(int InputTokens, int OutputTokens);

/// <summary>The model's reply.</summary>
/// <param name="Reply">Reply text.</param>
/// <param name="Model">Model that produced it.</param>
/// <param name="Usage">Token usage, when the provider reports it.</param>
public sealed record ChatResponse(string Reply, string Model, UsageResponse? Usage);
