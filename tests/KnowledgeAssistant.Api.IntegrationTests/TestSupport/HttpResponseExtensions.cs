using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

internal static class HttpResponseExtensions
{
    /// <summary>Reads the body as problem details, asserting the RFC 9457 media type.</summary>
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
