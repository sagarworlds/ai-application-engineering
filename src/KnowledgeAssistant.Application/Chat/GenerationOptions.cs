using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Application.Chat;

/// <summary>Sampling settings for model calls (configuration section <c>Generation</c>).</summary>
public sealed class GenerationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Generation";

    /// <summary>Default temperature for free-form chat.</summary>
    [Range(0.0, 2.0)]
    public double ChatTemperature { get; set; } = 0.7;

    /// <summary>
    /// Temperature for document-grounded answers. Kept low: we want the model to restate
    /// the sources, not to be creative.
    /// </summary>
    [Range(0.0, 2.0)]
    public double AnswerTemperature { get; set; } = 0.1;

    /// <summary>Upper bound on generated tokens per reply; caps cost and latency.</summary>
    [Range(16, 16_384)]
    public int MaxOutputTokens { get; set; } = 1024;
}
