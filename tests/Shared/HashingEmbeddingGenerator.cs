using System.Text.RegularExpressions;

using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.TestSupport;

/// <summary>
/// Deterministic stand-in for an embedding model: a hashed bag of words. Texts that share words
/// get similar vectors, which is enough to exercise retrieval without a network or a model.
/// </summary>
public sealed partial class HashingEmbeddingGenerator(string modelId = "hashing-test-embedding") : IEmbeddingGenerator
{
    private const int Dimensions = 128;

    private static readonly HashSet<string> StopWords =
        ["the", "a", "an", "is", "are", "of", "to", "in", "for", "and", "or", "what", "how", "do", "i", "must", "be", "at"];

    public string ModelId { get; } = modelId;

    /// <summary>Every text passed to the generator, in call order.</summary>
    public List<string> Inputs { get; } = [];

    public Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Inputs)
        {
            Inputs.AddRange(inputs);
        }

        return Task.FromResult<IReadOnlyList<float[]>>([.. inputs.Select(Embed)]);
    }

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            if (!StopWords.Contains(word.Value))
            {
                // string.GetHashCode is randomised per process; this stable hash keeps tests reproducible.
                var bucket = word.Value.Aggregate(17u, (hash, c) => (hash * 31) + c) % Dimensions;
                vector[bucket] += 1;
            }
        }

        return vector;
    }

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex Word();
}
