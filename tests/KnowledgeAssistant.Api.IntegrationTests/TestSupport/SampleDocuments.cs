using System.Net;
using System.Net.Http.Json;

using KnowledgeAssistant.Api.Contracts;

namespace KnowledgeAssistant.Api.IntegrationTests.TestSupport;

internal static class SampleDocuments
{
    /// <summary>A two-page travel policy as an ingestion request body.</summary>
    public static object TravelPolicy(string marker = "", string[]? allowedRoles = null) => new
    {
        fileName = "travel-policy.pdf",
        title = "Travel Policy 2026",
        allowedRoles,
        pages = new[]
        {
            new { pageNumber = 1, text = $"# Scope\n\nThis travel policy applies to all employees. {marker}" },
            new { pageNumber = 2, text = "# Booking\n\nFlights must be booked at least 14 days in advance.\n\n## Class\n\nEconomy class is required for flights under 6 hours." },
        },
    };

    public static async Task<DocumentResponse> IngestAsync(this HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/documents", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentResponse>())!;
    }

    /// <summary>A model reply that answers from source 1.</summary>
    public const string AnswerCitingSourceOne =
        """{"answerFound": true, "answer": "Flights must be booked at least 14 days in advance [1].", "citations": [1]}""";
}
