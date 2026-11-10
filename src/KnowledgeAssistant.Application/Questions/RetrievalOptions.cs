using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Application.Questions;

/// <summary>Retrieval settings (configuration section <c>Retrieval</c>).</summary>
public sealed class RetrievalOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Retrieval";

    /// <summary>Maximum number of passages given to the model per question.</summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Minimum cosine similarity for a passage to count as evidence. Below this the passage is
    /// noise; returning nothing lets the assistant say "I don't know" without calling the model.
    /// Tune per embedding model — similarity scales differ between models.
    /// </summary>
    [Range(-1.0, 1.0)]
    public double MinScore { get; set; } = 0.25;
}
