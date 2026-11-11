using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Application.Questions;

namespace KnowledgeAssistant.UnitTests.Application;

public sealed class GroundedAnswerParserTests
{
    [Fact]
    public void Parse_ValidAnswer_ReturnsAnswerAndCitations()
    {
        var parsed = GroundedAnswerParser.Parse(
            """{"answerFound": true, "answer": " Book 14 days ahead [1]. ", "citations": [1, 2]}""", sourceCount: 3);

        Assert.True(parsed.AnswerFound);
        Assert.Equal("Book 14 days ahead [1].", parsed.Answer);
        Assert.Equal([1, 2], parsed.Citations);
    }

    [Theory]
    [InlineData("```json\n{\"answerFound\": true, \"answer\": \"Yes [1]\", \"citations\": [1]}\n```")]
    [InlineData("```\n{\"answerFound\": true, \"answer\": \"Yes [1]\", \"citations\": [1]}\n```")]
    [InlineData("  \n{\"answerFound\": true, \"answer\": \"Yes [1]\", \"citations\": [1]}\n  ")]
    public void Parse_ToleratesCodeFencesAndWhitespace(string output)
    {
        var parsed = GroundedAnswerParser.Parse(output, sourceCount: 1);

        Assert.Equal("Yes [1]", parsed.Answer);
    }

    [Fact]
    public void Parse_AnswerNotFound_DiscardsEverythingElseTheModelWrote()
    {
        var parsed = GroundedAnswerParser.Parse(
            """{"answerFound": false, "answer": "But I think it is probably 30 days.", "citations": [1]}""", sourceCount: 1);

        Assert.Same(ParsedAnswer.NotFound, parsed);
        Assert.Empty(parsed.Answer);
        Assert.Empty(parsed.Citations);
    }

    [Fact]
    public void Parse_DropsCitationsThatWereNeverSupplied_AndDuplicates()
    {
        var parsed = GroundedAnswerParser.Parse(
            """{"answerFound": true, "answer": "x", "citations": [2, 0, -1, 7, 2, 1.5, "1", 3]}""", sourceCount: 3);

        Assert.Equal([2, 3], parsed.Citations);
    }

    [Theory]
    [InlineData("The answer is 14 days.", "not valid JSON")]
    [InlineData("""{"answerFound": true, "answer": "cut off mid-sen""", "not valid JSON")]
    [InlineData("[1, 2]", "not a JSON object")]
    [InlineData("""{"answer": "x", "citations": [1]}""", "answerFound")]
    [InlineData("""{"answerFound": "yes", "answer": "x", "citations": [1]}""", "answerFound")]
    [InlineData("""{"answerFound": true, "answer": "  ", "citations": [1]}""", "answer")]
    [InlineData("""{"answerFound": true, "citations": [1]}""", "answer")]
    [InlineData("""{"answerFound": true, "answer": "x"}""", "citations")]
    [InlineData("""{"answerFound": true, "answer": "x", "citations": "1"}""", "citations")]
    public void Parse_MalformedOutput_Throws(string output, string expectedReason)
    {
        var exception = Assert.Throws<LlmOutputInvalidException>(() => GroundedAnswerParser.Parse(output, sourceCount: 2));

        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
    }
}
