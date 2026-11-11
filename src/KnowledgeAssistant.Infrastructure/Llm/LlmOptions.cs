using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Llm;

/// <summary>How structured (JSON) output is requested from the provider.</summary>
public enum StructuredOutputMode
{
    /// <summary><c>response_format: json_schema</c> — the provider constrains output to the schema.</summary>
    JsonSchema,

    /// <summary><c>response_format: json_object</c> — valid JSON, schema described in the prompt only.</summary>
    JsonObject,

    /// <summary>No <c>response_format</c>; rely on the prompt. For providers that support neither.</summary>
    None,
}

/// <summary>
/// Settings for an OpenAI-compatible model provider (configuration section <c>Llm</c>).
/// Works with Azure OpenAI (v1 endpoint), OpenAI, Ollama and similar.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Llm";

    /// <summary>
    /// Base URL of the API, e.g. <c>https://my-resource.openai.azure.com/openai/v1/</c> or
    /// <c>https://api.openai.com/v1/</c>. Empty means "not configured": model calls fail with 503.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key. Supply via user-secrets or the <c>Llm__ApiKey</c> environment variable, never appsettings.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Header carrying the key. <c>Authorization</c> sends <c>Bearer &lt;key&gt;</c>; any other name
    /// (Azure OpenAI: <c>api-key</c>) sends the raw key.
    /// </summary>
    [Required]
    public string ApiKeyHeaderName { get; set; } = "Authorization";

    /// <summary>Chat model (Azure OpenAI: deployment name).</summary>
    public string ChatModel { get; set; } = string.Empty;

    /// <summary>Embedding model (Azure OpenAI: deployment name).</summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>Maximum time for a non-streaming call, including reading the response.</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:10:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum total duration of a streaming call.</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:30:00")]
    public TimeSpan StreamTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Name of the output-token limit parameter. Most compatible providers use <c>max_tokens</c>;
    /// newer OpenAI/Azure models require <c>max_completion_tokens</c>.
    /// </summary>
    [RegularExpression("^(max_tokens|max_completion_tokens)$")]
    public string MaxTokensParameterName { get; set; } = "max_tokens";

    /// <summary>How structured output is requested.</summary>
    public StructuredOutputMode StructuredOutputMode { get; set; } = StructuredOutputMode.JsonSchema;

    /// <summary>True when enough is set to call the chat model.</summary>
    public bool IsChatConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ChatModel);

    /// <summary>True when enough is set to call the embedding model.</summary>
    public bool IsEmbeddingConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(EmbeddingModel);
}

/// <summary>Cross-field validation of <see cref="LlmOptions"/> that attributes cannot express.</summary>
internal sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            // Not configured is a valid state: the API still serves health and metadata endpoints.
            return ValidateOptionsResult.Success;
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return ValidateOptionsResult.Fail("Llm:BaseUrl must be an absolute http(s) URL.");
        }

        // Prompts contain document text and the request carries the API key: refuse cleartext
        // transport to anything but a local development endpoint.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            return ValidateOptionsResult.Fail("Llm:BaseUrl must use https unless it points to localhost.");
        }

        return ValidateOptionsResult.Success;
    }
}
