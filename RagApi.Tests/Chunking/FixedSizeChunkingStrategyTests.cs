using RagApi.Services.Chunking;
using RagApi.Services.Pdf;

namespace RagApi.Tests.Chunking;

// LEARNING NOTE — Why [Fact] vs [Theory]?
// [Fact]   = a test that runs once with fixed inputs. Use when there is only one scenario.
// [Theory] = a test that runs multiple times with different inputs ([InlineData]).
//            It avoids copy-pasting almost-identical tests.

public class FixedSizeChunkingStrategyTests
{
    // -------------------------------------------------------------------------
    // Helper — builds a single-page input quickly
    // -------------------------------------------------------------------------
    private static IReadOnlyList<ParsedPage> OnePage(string text, int pageNumber = 1)
        => [new ParsedPage(pageNumber, text)];

    // =========================================================================
    // BASIC CHUNKING BEHAVIOUR
    // =========================================================================

    [Fact]
    public void Chunk_EmptyPages_ReturnsNoChunks()
    {
        // Arrange
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 5, overlap: 1);

        // Act
        var result = strategy.Chunk([]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_PageWithNoText_ReturnsNoChunks()
    {
        // Arrange
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 5, overlap: 1);

        // Act
        var result = strategy.Chunk(OnePage("   "));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_FewWordsUnderChunkSize_ReturnsSingleChunk()
    {
        // Arrange — 3 words, chunkSize 10 → should produce 1 chunk
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 10, overlap: 2);

        // Act
        var result = strategy.Chunk(OnePage("hello world foo"));

        // Assert
        Assert.Single(result);
        Assert.Equal("hello world foo", result[0].Text);
    }

    [Fact]
    public void Chunk_ExactlyChunkSizeWords_ReturnsSingleChunk()
    {
        // Arrange — 5 words, chunkSize 5, no overlap → exactly 1 chunk
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 5, overlap: 0);
        var text = "one two three four five";

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert
        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public void Chunk_MoreWordsThanChunkSize_ProducesMultipleChunks()
    {
        // Arrange — 10 words, chunkSize 5, overlap 0 → 2 chunks
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 5, overlap: 0);
        var text = "w1 w2 w3 w4 w5 w6 w7 w8 w9 w10";

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("w1 w2 w3 w4 w5", result[0].Text);
        Assert.Equal("w6 w7 w8 w9 w10", result[1].Text);
    }

    // =========================================================================
    // OVERLAP BEHAVIOUR
    // =========================================================================

    [Fact]
    public void Chunk_WithOverlap_LastWordsOfChunkAppearsAtStartOfNext()
    {
        // Arrange — chunkSize 4, overlap 2 → step = 2
        // Words:  w1 w2 w3 w4 w5 w6
        // Chunk0: w1 w2 w3 w4
        // Chunk1: w3 w4 w5 w6  (overlap = last 2 of previous)
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 4, overlap: 2);
        var text = "w1 w2 w3 w4 w5 w6";

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.StartsWith("w3 w4", result[1].Text);
    }

    // =========================================================================
    // ORDINAL NUMBERING
    // =========================================================================

    [Fact]
    public void Chunk_OrdinalStartsAtZeroAndIncrementsSequentially()
    {
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 3, overlap: 0);
        var text = "a b c d e f g h i"; // 9 words → 3 chunks

        var result = strategy.Chunk(OnePage(text));

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].Ordinal);
        Assert.Equal(1, result[1].Ordinal);
        Assert.Equal(2, result[2].Ordinal);
    }

    // =========================================================================
    // PAGE NUMBER CITATION
    // =========================================================================

    [Fact]
    public void Chunk_SinglePage_AllChunksCiteCorrectPageNumber()
    {
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 3, overlap: 0);
        var pages = new List<ParsedPage> { new(7, "a b c d e f") };

        var result = strategy.Chunk(pages);

        Assert.All(result, c => Assert.Equal(7, c.PageNumber));
    }

    [Fact]
    public void Chunk_MultiplePages_ChunkCitesPageWhereItStarts()
    {
        // Page 1: 3 words (fills exactly one chunk)
        // Page 2: 3 words (fills exactly one chunk)
        var strategy = new FixedSizeChunkingStrategy(chunkSize: 3, overlap: 0);
        var pages = new List<ParsedPage>
        {
            new(1, "a b c"),
            new(2, "d e f")
        };

        var result = strategy.Chunk(pages);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].PageNumber);
        Assert.Equal(2, result[1].PageNumber);
    }

    // =========================================================================
    // CONSTRUCTOR VALIDATION
    // =========================================================================

    [Fact]
    public void Constructor_ZeroChunkSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedSizeChunkingStrategy(chunkSize: 0, overlap: 0));
    }

    [Fact]
    public void Constructor_NegativeOverlap_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedSizeChunkingStrategy(chunkSize: 10, overlap: -1));
    }

    [Fact]
    public void Constructor_OverlapEqualToChunkSize_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new FixedSizeChunkingStrategy(chunkSize: 5, overlap: 5));
    }

    // =========================================================================
    // STRATEGY NAME
    // =========================================================================

    [Fact]
    public void StrategyName_ReturnsFixedSize()
    {
        var strategy = new FixedSizeChunkingStrategy();
        Assert.Equal("fixed-size", strategy.StrategyName);
    }

    // =========================================================================
    // THEORY EXAMPLE — same test, multiple inputs
    // =========================================================================

    [Theory]
    [InlineData(5, 0, 10, 2)]   // 10 words, chunkSize 5, overlap 0 → 2 chunks
    [InlineData(5, 2, 10, 3)]   // step=3 → starts at 0,3,6 → 3 chunks
    [InlineData(3, 0, 9,  3)]   // 9 words, chunkSize 3, overlap 0 → 3 chunks
    public void Chunk_VariousInputs_ProducesExpectedChunkCount(
        int chunkSize, int overlap, int wordCount, int expectedChunks)
    {
        // Arrange
        var strategy = new FixedSizeChunkingStrategy(chunkSize, overlap);
        var text = string.Join(" ", Enumerable.Range(1, wordCount).Select(i => $"w{i}"));

        // Act
        var result = strategy.Chunk(OnePage(text));

        // Assert
        Assert.Equal(expectedChunks, result.Count);
    }
}
