using System.Net;
using System.Net.Http.Json;
using System.Text;

using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Application.Questions;

namespace KnowledgeAssistant.Api.IntegrationTests;

public sealed class QuestionEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Ask_WithNoSupportingDocuments_ReturnsSafeUnknownAnswer()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/questions", new { question = "What is the travel policy?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = await response.Content.ReadFromJsonAsync<QuestionResponse>();
        Assert.NotNull(answer);
        Assert.False(answer.AnswerFound);
        Assert.Equal(GroundedAnswer.UnknownAnswerText, answer.Answer);
        Assert.Empty(answer.Citations);
        Assert.NotEqual(Guid.Empty, answer.ConversationId);
    }

    [Theory]
    [InlineData("{}", "Question")]
    [InlineData("""{"question":""}""", "Question")]
    [InlineData("""{"question":"hi"}""", "Question")]
    public async Task Ask_InvalidBody_Returns400WithFieldErrors(string body, string expectedField)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/questions", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _), problem.ToString());
    }

    [Fact]
    public async Task Ask_QuestionTooLong_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/questions", new { question = new string('x', QuestionRequest.MaxQuestionLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_MalformedJson_Returns400Problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/questions", Json("{ not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await response.ReadProblemAsync();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
