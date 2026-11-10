using KnowledgeAssistant.Application.Ingestion;

using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.UnitTests.Application;

public sealed class DocumentAwareChunkerTests
{
    [Fact]
    public void Chunk_ShortPage_ProducesOneChunkWithPageNumber()
    {
        var chunks = Chunker().Chunk([new PageContent(7, "Flights must be booked 14 days in advance.")]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(7, chunk.PageNumber);
        Assert.Null(chunk.Section);
        Assert.Equal("Flights must be booked 14 days in advance.", chunk.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n \t ")]
    [InlineData("# Only A Heading")]
    public void Chunk_NoBodyText_ProducesNoChunks(string text)
    {
        Assert.Empty(Chunker().Chunk([new PageContent(1, text)]));
    }

    [Fact]
    public void Chunk_MarkdownHeadings_BecomeSectionMetadataAndAreNotBodyText()
    {
        const string page = """
            # Travel Policy

            This policy applies to all employees.

            ## Booking ##

            Flights must be booked 14 days in advance.
            """;

        var chunks = Chunker().Chunk([new PageContent(1, page)]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(("Travel Policy", "This policy applies to all employees."), (chunks[0].Section, chunks[0].Content));
        Assert.Equal(("Booking", "Flights must be booked 14 days in advance."), (chunks[1].Section, chunks[1].Content));
    }

    [Fact]
    public void Chunk_PlainTextHeadings_NumberedAndAllCaps_AreDetected()
    {
        const string page =
            "EXPENSE POLICY\nReceipts are required for every claim.\n\n" +
            "2.1 Meal Allowances\nThe daily meal allowance is 45 euros.";

        var chunks = Chunker().Chunk([new PageContent(1, page)]);

        Assert.Equal(["EXPENSE POLICY", "2.1 Meal Allowances"], chunks.Select(c => c.Section));
        Assert.Equal("The daily meal allowance is 45 euros.", chunks[1].Content);
    }

    [Fact]
    public void Chunk_NumberedListAndOrdinarySentences_AreNotMistakenForHeadings()
    {
        const string page =
            "To claim expenses\n\n1. Submit the form\n2. Attach receipts\n\nThe finance team replies within 5 days.\n\nUSA and UK offices differ.";

        var chunks = Chunker().Chunk([new PageContent(1, page)]);

        Assert.All(chunks, c => Assert.Null(c.Section));
        Assert.Contains("1. Submit the form\n2. Attach receipts", string.Join("\n\n", chunks.Select(c => c.Content)), StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_SectionCarriesOverToFollowingPages_UntilTheNextHeading()
    {
        var chunks = Chunker().Chunk(
        [
            new PageContent(1, "# Booking\n\nFlights must be booked early."),
            new PageContent(2, "Trains may be booked late."),
            new PageContent(3, "# Expenses\n\nKeep receipts."),
        ]);

        Assert.Equal([("Booking", 1), ("Booking", 2), ("Expenses", 3)], chunks.Select(c => (c.Section!, c.PageNumber)));
        Assert.Equal([0, 1, 2], chunks.Select(c => c.Index));
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(200, 100)]
    [InlineData(300, 60)]
    [InlineData(1200, 200)]
    public void Chunk_LongText_NeverExceedsChunkSize_AndLosesNoWords(int size, int overlap)
    {
        var words = Enumerable.Range(0, 1500).Select(i => $"word{i}{(i % 9 == 8 ? "." : string.Empty)}").ToList();
        // Mix paragraph breaks, line breaks and one long unpunctuated run.
        var text = string.Join(' ', words.Take(700)) + "\n\n" + string.Join('\n', words.Skip(700).Take(300)) + "\n\n"
            + string.Join(' ', words.Skip(1000)).Replace(".", string.Empty, StringComparison.Ordinal);

        var chunks = Chunker(size, overlap).Chunk([new PageContent(1, text)]);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.InRange(c.Content.Length, 1, size));
        var chunkedWords = chunks.SelectMany(c => c.Content.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.TrimEnd('.')).ToHashSet();
        Assert.All(words, w => Assert.Contains(w.TrimEnd('.'), chunkedWords));
    }

    [Fact]
    public void Chunk_ConsecutiveChunks_OverlapSoBoundaryFactsStayTogether()
    {
        var sentences = Enumerable.Range(1, 40).Select(i => $"Rule number {i} says that item {i} is required.");
        var chunks = Chunker(size: 300, overlap: 80).Chunk([new PageContent(1, string.Join(' ', sentences))]);

        Assert.True(chunks.Count > 2);
        for (var i = 1; i < chunks.Count; i++)
        {
            // The next chunk starts with text taken from the end of the previous one.
            var opening = chunks[i].Content[..30];
            Assert.Contains(opening, chunks[i - 1].Content, StringComparison.Ordinal);
            Assert.False(char.IsWhiteSpace(chunks[i].Content[0]));
        }
    }

    [Fact]
    public void Chunk_NoOverlapAcrossPagesOrSections()
    {
        var pageOne = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"Alpha sentence {i} ends here."));
        var pageTwo = "# New Section\n\n" + string.Join(' ', Enumerable.Range(1, 30).Select(i => $"Beta sentence {i} ends here."));

        var chunks = Chunker(size: 300, overlap: 80).Chunk([new PageContent(1, pageOne), new PageContent(2, pageTwo)]);

        Assert.All(chunks.Where(c => c.PageNumber == 2), c => Assert.DoesNotContain("Alpha", c.Content, StringComparison.Ordinal));
        Assert.All(chunks.Where(c => c.PageNumber == 1), c => Assert.DoesNotContain("Beta", c.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void Chunk_SingleTokenLongerThanChunkSize_IsHardSplitWithoutLoss()
    {
        var token = new string('x', 950);

        var chunks = Chunker(size: 200, overlap: 0).Chunk([new PageContent(1, token)]);

        Assert.All(chunks, c => Assert.True(c.Content.Length <= 200));
        Assert.Equal(950, chunks.Sum(c => c.Content.Length));
    }

    [Theory]
    [InlineData(1200, 200, true)]
    [InlineData(400, 200, true)]
    [InlineData(400, 201, false)]
    public void OptionsValidator_RejectsOverlapAboveHalfTheChunkSize(int size, int overlap, bool expectedValid)
    {
        var result = new IngestionOptionsValidator().Validate(
            null, new IngestionOptions { ChunkSizeCharacters = size, ChunkOverlapCharacters = overlap });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    private static DocumentAwareChunker Chunker(int size = 1200, int overlap = 200) =>
        new(Options.Create(new IngestionOptions { ChunkSizeCharacters = size, ChunkOverlapCharacters = overlap }));
}
