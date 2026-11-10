using System.Net;
using System.Net.Http.Json;

using KnowledgeAssistant.Api.Contracts;
using KnowledgeAssistant.Api.IntegrationTests.TestSupport;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Questions;
using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Api.IntegrationTests;

/// <summary>The main user journey: ingest a document, then ask questions about it.</summary>
public sealed class IngestionAndQuestionFlowTests : IClassFixture<ApiFactory>
{
    private const string BookingQuestion = "How many days in advance must flights be booked?";
    private readonly ApiFactory _factory;

    public IngestionAndQuestionFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.ChatModel.Reset();
    }

    [Fact]
    public async Task Ingest_ReturnsCreatedDocumentWithLocation_AndItBecomesListed()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);

        var response = await contributor.PostAsJsonAsync("/api/v1/documents", SampleDocuments.TravelPolicy());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.NotNull(created);
        Assert.Equal(("travel-policy.pdf", "Travel Policy 2026", 2, 3), (created.FileName, created.Title, created.PageCount, created.ChunkCount));
        Assert.EndsWith($"/api/v1/documents/{created.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);

        var fetched = await contributor.GetFromJsonAsync<DocumentResponse>(response.Headers.Location);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Ask_AfterIngestion_ReturnsAnswerWithCitationPointingAtFilePageAndSection()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        using var reader = _factory.CreateClientFor(tenant, AppRoles.Reader);
        var document = await contributor.IngestAsync(SampleDocuments.TravelPolicy());
        _factory.ChatModel.Reply = _ => SampleDocuments.AnswerCitingSourceOne;

        var answer = await AskAsync(reader, BookingQuestion);

        Assert.True(answer.AnswerFound);
        Assert.Equal("Flights must be booked at least 14 days in advance [1].", answer.Answer);
        var citation = Assert.Single(answer.Citations);
        Assert.Equal((document.Id, "travel-policy.pdf", "Travel Policy 2026", 2, "Booking"),
            (citation.DocumentId, citation.FileName, citation.Title, citation.PageNumber, citation.Section));
        Assert.Contains("14 days", citation.Snippet, StringComparison.Ordinal);

        // The prompt carried the retrieved passage as a numbered source, best match first.
        var prompt = Assert.Single(_factory.ChatModel.Requests).Messages[^1].Content;
        Assert.Contains("""<source id="1" file="travel-policy.pdf" page="2" section="Booking">""", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_QuestionUnrelatedToAnyDocument_ReturnsUnknown_WithoutCallingTheModel()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());

        var answer = await AskAsync(contributor, "Which programming languages does the cafeteria menu support?");

        Assert.False(answer.AnswerFound);
        Assert.Equal(GroundedAnswer.UnknownAnswerText, answer.Answer);
        Assert.Empty(answer.Citations);
        Assert.Empty(_factory.ChatModel.Requests);
    }

    [Fact]
    public async Task Ask_ModelSaysEvidenceIsInsufficient_ReturnsFixedUnknownAnswer()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());
        _factory.ChatModel.Reply = _ => """{"answerFound": false, "answer": "", "citations": []}""";

        var answer = await AskAsync(contributor, BookingQuestion);

        Assert.False(answer.AnswerFound);
        Assert.Equal(GroundedAnswer.UnknownAnswerText, answer.Answer);
    }

    [Fact]
    public async Task Ask_ModelKeepsReturningGarbage_Returns502Problem()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());
        _factory.ChatModel.Reply = _ => "I think it is two weeks.";

        var response = await contributor.PostAsJsonAsync("/api/v1/questions", new { question = BookingQuestion });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await response.ReadProblemAsync();
        Assert.Equal(2, _factory.ChatModel.Requests.Count);
    }

    [Fact]
    public async Task Ask_FollowUpInSameConversation_SendsPreviousTurnToTheModel()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());
        _factory.ChatModel.Reply = _ => SampleDocuments.AnswerCitingSourceOne;

        var first = await AskAsync(contributor, BookingQuestion);
        var second = await AskAsync(contributor, "Must flights be booked in economy class?", first.ConversationId);

        Assert.Equal(first.ConversationId, second.ConversationId);
        var followUp = _factory.ChatModel.Requests[1].Messages;
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], followUp.Select(m => m.Role));
        Assert.Equal(BookingQuestion, followUp[1].Content);
    }

    [Fact]
    public async Task Ingest_SameContentTwice_Returns409()
    {
        using var contributor = _factory.CreateClientFor(NewTenant(), AppRoles.Contributor);
        await contributor.IngestAsync(SampleDocuments.TravelPolicy());

        var duplicate = await contributor.PostAsJsonAsync("/api/v1/documents", SampleDocuments.TravelPolicy());

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await duplicate.ReadProblemAsync();
    }

    [Fact]
    public async Task Ingest_ByReader_Returns403()
    {
        using var reader = _factory.CreateClientFor(NewTenant(), AppRoles.Reader);

        var response = await reader.PostAsJsonAsync("/api/v1/documents", SampleDocuments.TravelPolicy());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"pages":[{"pageNumber":1,"text":"x"}]}""", "FileName")]
    [InlineData("""{"fileName":"a.pdf","pages":[]}""", "Pages")]
    [InlineData("""{"fileName":"a.pdf"}""", "Pages")]
    [InlineData("""{"fileName":"a.pdf","pages":[{"pageNumber":0,"text":"x"}]}""", "Pages[0].PageNumber")]
    [InlineData("""{"fileName":"a.pdf","pages":[{"pageNumber":1,"text":"  "}]}""", "pages")]
    [InlineData("""{"fileName":"a.pdf","pages":[{"pageNumber":1,"text":"x"},{"pageNumber":1,"text":"y"}]}""", "pages")]
    public async Task Ingest_InvalidBody_Returns400NamingTheField(string json, string expectedField)
    {
        using var contributor = _factory.CreateClientFor(NewTenant(), AppRoles.Contributor);

        var response = await contributor.PostAsync(
            "/api/v1/documents", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _), problem.ToString());
    }

    [Fact]
    public async Task Ingest_TenantInBodyIsIgnored_DocumentBelongsToTheTokensTenant()
    {
        var tenant = NewTenant();
        var victim = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        using var victimReader = _factory.CreateClientFor(victim, AppRoles.Admin);

        var created = await contributor.IngestAsync(new
        {
            fileName = "planted.pdf",
            tenantId = victim,
            pages = new[] { new { pageNumber = 1, text = "Flights must be booked 1 day in advance." } },
        });

        Assert.Equal(HttpStatusCode.NotFound, (await victimReader.GetAsync($"/api/v1/documents/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await contributor.GetAsync($"/api/v1/documents/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesDocumentFromListingAndFromRetrieval()
    {
        var tenant = NewTenant();
        using var contributor = _factory.CreateClientFor(tenant, AppRoles.Contributor);
        var document = await contributor.IngestAsync(SampleDocuments.TravelPolicy());

        var deleted = await contributor.DeleteAsync($"/api/v1/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await contributor.GetAsync($"/api/v1/documents/{document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await contributor.DeleteAsync($"/api/v1/documents/{document.Id}")).StatusCode);
        Assert.False((await AskAsync(contributor, BookingQuestion)).AnswerFound);
        Assert.Empty(_factory.ChatModel.Requests);
    }

    private static string NewTenant() => $"tenant-{Guid.NewGuid():N}";

    private static async Task<QuestionResponse> AskAsync(HttpClient client, string question, Guid? conversationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/questions", new { question, conversationId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuestionResponse>())!;
    }
}
