using System.Text.Json;

using KnowledgeAssistant.Application.Exceptions;

namespace KnowledgeAssistant.Application.Questions;

/// <summary>The model's structured reply after validation.</summary>
/// <param name="AnswerFound">Whether the model claims the sources answer the question.</param>
/// <param name="Answer">Answer text; empty when <paramref name="AnswerFound"/> is false.</param>
/// <param name="Citations">Distinct, valid source numbers (1-based) in the order given.</param>
internal sealed record ParsedAnswer(bool AnswerFound, string Answer, IReadOnlyList<int> Citations)
{
    public static ParsedAnswer NotFound { get; } = new(false, string.Empty, []);
}

/// <summary>
/// Validates model output server-side. Even with schema-constrained output the model can cite
/// sources that were never supplied, so nothing from the model is trusted until it passes here.
/// </summary>
internal static class GroundedAnswerParser
{
    /// <summary>JSON Schema the model is asked to follow.</summary>
    public const string ResponseSchemaJson = """
        {
          "type": "object",
          "properties": {
            "answerFound": { "type": "boolean" },
            "answer": { "type": "string" },
            "citations": { "type": "array", "items": { "type": "integer" } }
          },
          "required": ["answerFound", "answer", "citations"],
          "additionalProperties": false
        }
        """;

    /// <summary>Parses and validates the model's reply.</summary>
    /// <param name="modelOutput">Raw text returned by the model.</param>
    /// <param name="sourceCount">Number of sources that were supplied (valid citations are 1..N).</param>
    /// <returns>The validated answer.</returns>
    /// <exception cref="LlmOutputInvalidException">The output is not the expected JSON object.</exception>
    public static ParsedAnswer Parse(string modelOutput, int sourceCount)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripCodeFence(modelOutput));
        }
        catch (JsonException)
        {
            throw new LlmOutputInvalidException("not valid JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new LlmOutputInvalidException("not a JSON object");
            }

            if (!root.TryGetProperty("answerFound", out var found)
                || found.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new LlmOutputInvalidException("'answerFound' is missing or not a boolean");
            }

            if (!found.GetBoolean())
            {
                // Whatever else the model wrote is discarded; the caller substitutes the fixed unknown text.
                return ParsedAnswer.NotFound;
            }

            if (!root.TryGetProperty("answer", out var answer)
                || answer.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(answer.GetString()))
            {
                throw new LlmOutputInvalidException("'answer' is missing or empty");
            }

            if (!root.TryGetProperty("citations", out var citations) || citations.ValueKind != JsonValueKind.Array)
            {
                throw new LlmOutputInvalidException("'citations' is missing or not an array");
            }

            // Citations to sources that were not supplied are hallucinated: drop them rather than fail,
            // and let the caller decide what an answer with no remaining evidence means.
            var valid = citations.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out _))
                .Select(c => c.GetInt32())
                .Where(n => n >= 1 && n <= sourceCount)
                .Distinct()
                .ToList();

            return new ParsedAnswer(true, answer.GetString()!.Trim(), valid);
        }
    }

    // Models without native structured output often wrap JSON in a Markdown code fence.
    private static string StripCodeFence(string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed.ToString();
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd < 0 || closingFence <= firstLineEnd
            ? trimmed.ToString()
            : trimmed[(firstLineEnd + 1)..closingFence].Trim().ToString();
    }
}
