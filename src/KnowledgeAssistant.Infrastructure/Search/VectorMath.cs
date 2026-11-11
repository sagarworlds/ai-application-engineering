namespace KnowledgeAssistant.Infrastructure.Search;

internal static class VectorMath
{
    /// <summary>
    /// Cosine similarity: the cosine of the angle between two vectors, in [-1, 1]. It compares
    /// direction and ignores magnitude, which is what embedding models are trained for.
    /// </summary>
    /// <returns>The similarity, or <c>null</c> when the vectors differ in size or one has zero length.</returns>
    public static double? CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return null;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        return normA == 0 || normB == 0 ? null : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
