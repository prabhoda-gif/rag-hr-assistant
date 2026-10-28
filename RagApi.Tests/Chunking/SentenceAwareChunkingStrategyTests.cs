using RagApi.Services.Chunking;
using RagApi.Services.Pdf;

namespace RagApi.Tests.Chunking;

public class SentenceAwareChunkingStrategyTests
{
    private static IReadOnlyList<ParsedPage> OnePage(string text, int pageNumber = 1)
        => [new ParsedPage(pageNumber, text)];

    // =========================================================================
    // BASIC BEHAVIOUR
    // =========================================================================

    [Fact]
    public void Chunk_EmptyPages_ReturnsNoChunks()
    {
        var strategy = new SentenceAwareChunkingStrategy();

        var result = strategy.Chunk([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_BlankPageText_ReturnsNoChunks()
    {
        var strategy = new SentenceAwareChunkingStrategy();

        var result = strategy.Chunk(OnePage("   "));

        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_SingleShortSentence_ReturnsSingleChunk()
    {
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 50);
        var text = "Employees are entitled to 21 days of annual leave.";

        var result = strategy.Chunk(OnePage(text));

        Assert.Single(result);
        Assert.Contains("21 days", result[0].Text);
    }

    [Fact]
    public void Chunk_MultipleSentencesBelowTarget_CombinesIntoOneChunk()
    {
        // 3 short sentences, well under targetWords=100 → should be 1 chunk
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 100);
        var text = "First sentence here. Second sentence here. Third sentence here.";

        var result = strategy.Chunk(OnePage(text));

        Assert.Single(result);
    }

    // =========================================================================
    // SENTENCE BOUNDARY RESPECT
    // =========================================================================

    [Fact]
    public void Chunk_LongText_DoesNotSplitMidSentence()
    {
        // Arrange — two long sentences that together exceed targetWords=10
        // Strategy should emit chunk 1 after the first sentence, not mid-word
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 10, overlapSentences: 0);
        var text = "The employee shall receive full pay during maternity leave. " +
                   "Paternity leave is granted for a period of fifteen working days.";

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert — each chunk should end with sentence-ending punctuation
        Assert.All(result, chunk =>
        {
            var trimmed = chunk.Text.TrimEnd();
            Assert.True(
                trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!'),
                $"Chunk does not end with sentence punctuation: '{trimmed}'");
        });
    }

    // =========================================================================
    // OVERLAP BEHAVIOUR
    // =========================================================================

    [Fact]
    public void Chunk_WithOverlapSentences_LastSentenceOfChunkAppearsInNext()
    {
        // Arrange — targetWords=10 forces a split after first sentence
        // overlapSentences=1 means sentence 1 should also appear at start of chunk 2
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 10, overlapSentences: 1);
        var sentence1 = "The company provides medical insurance to all permanent employees.";
        var sentence2 = "Coverage extends to spouse and two dependent children.";
        var text = $"{sentence1} {sentence2}";

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert — chunk 2 should start with (or contain) the overlap from sentence 1
        Assert.True(result.Count >= 2, "Expected at least 2 chunks");
        Assert.Contains("medical insurance", result[1].Text);
    }

    [Fact]
    public void Chunk_ZeroOverlapSentences_NoRepetitionBetweenChunks()
    {
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 8, overlapSentences: 0);
        var text = "First sentence is here now. Second sentence follows right after. Third one ends it.";

        var result = strategy.Chunk(OnePage(text));

        // With zero overlap, chunk 0 last word should NOT appear in chunk 1
        if (result.Count >= 2)
        {
            var chunk0Words = result[0].Text.Split(' ');
            var lastWordChunk0 = chunk0Words.Last();
            Assert.DoesNotContain(lastWordChunk0, result[1].Text.Split(' ').Take(3));
        }
    }

    // =========================================================================
    // ORDINAL NUMBERING
    // =========================================================================

    [Fact]
    public void Chunk_OrdinalStartsAtZeroAndIncrementsSequentially()
    {
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 5, overlapSentences: 0);
        var text = "First sentence done. Second sentence done. Third sentence done.";

        var result = strategy.Chunk(OnePage(text));

        for (int i = 0; i < result.Count; i++)
            Assert.Equal(i, result[i].Ordinal);
    }

    // =========================================================================
    // PAGE NUMBER CITATION
    // =========================================================================

    [Fact]
    public void Chunk_SinglePage_AllChunksCiteCorrectPageNumber()
    {
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 5, overlapSentences: 0);
        var pages = new List<ParsedPage> { new(3, "Hello world this is fine. Another sentence follows here.") };

        var result = strategy.Chunk(pages);

        Assert.All(result, c => Assert.Equal(3, c.PageNumber));
    }

    [Fact]
    public void Chunk_MultiplePages_EachPageChunksCiteOwnPageNumber()
    {
        var strategy = new SentenceAwareChunkingStrategy(targetWords: 100, overlapSentences: 0);
        var pages = new List<ParsedPage>
        {
            new(1, "Page one content sentence here."),
            new(2, "Page two content sentence here.")
        };

        var result = strategy.Chunk(pages);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].PageNumber);
        Assert.Equal(2, result[1].PageNumber);
    }

    // =========================================================================
    // STRATEGY NAME
    // =========================================================================

    [Fact]
    public void StrategyName_ReturnsSentenceAware()
    {
        var strategy = new SentenceAwareChunkingStrategy();
        Assert.Equal("sentence-aware", strategy.StrategyName);
    }
}
