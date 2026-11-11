using System.Net.Http.Json;
using System.Text.Json;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Llm;

/// <summary>
/// <see cref="IEmbeddingGenerator"/> over the OpenAI-compatible <c>POST embeddings</c> API.
/// Created through <see cref="IHttpClientFactory"/> as a typed client.
/// </summary>
internal sealed partial class OpenAiCompatibleEmbeddingGenerator(
    HttpClient httpClient,
    IOptions<LlmOptions> options,
    ILogger<OpenAiCompatibleEmbeddingGenerator> logger) : IEmbeddingGenerator
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options = options.Value;

    public string ModelId => _options.EmbeddingModel;

    public async Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (!_options.IsEmbeddingConfigured)
        {
            LogNotConfigured(logger);
            throw new LlmUnavailableException("The embedding model is not configured.");
        }

        if (inputs.Count == 0)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        try
        {
            using var response = await httpClient
                .PostAsJsonAsync("embeddings", new { model = _options.EmbeddingModel, input = inputs }, timeout.Token)
                .ConfigureAwait(false);
            LlmHttp.EnsureSuccess(response, logger, "embeddings");

            var payload = await response.Content
                .ReadFromJsonAsync<EmbeddingPayload>(PayloadOptions, timeout.Token)
                .ConfigureAwait(false);

            // The API may return items out of order; "index" ties each vector to its input.
            var vectors = payload?.Data?
                .Where(item => item.Embedding is { Length: > 0 })
                .OrderBy(item => item.Index)
                .Select(item => item.Embedding!)
                .ToList();

            if (vectors is null || vectors.Count != inputs.Count)
            {
                throw new LlmUnavailableException("The embedding model returned an unexpected number of vectors.");
            }

            return vectors;
        }
        catch (Exception exception) when (LlmHttp.Translate(exception, cancellationToken, _options.RequestTimeout) is { } translated)
        {
            throw translated;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Embedding model is not configured: set Llm:BaseUrl and Llm:EmbeddingModel")]
    private static partial void LogNotConfigured(ILogger logger);

    private sealed record EmbeddingPayload(List<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(int Index, float[]? Embedding);
}
