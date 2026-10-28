using RagApi.Services.Pdf;

namespace RagApi.Services.Chunking;

/// <summary>
/// Splits text into fixed-size word windows with configurable overlap.
///
/// LEARNING NOTE — Fixed-size chunking is the baseline to beat.
/// It's fast and predictable, but it blindly cuts across sentence and
/// paragraph boundaries. Use it to establish your Recall@k baseline,
/// then compare against SentenceAwareChunkingStrategy.
///
/// "ChunkSize" here is measured in words, not tokens. Word count is a
/// reasonable approximation (1 token ≈ 0.75 words for English), but it
/// is NOT exact. Token-exact chunking requires a tokenizer library such
/// as SharpToken. This is documented here so you can explain the trade-off
/// in an interview.
/// </summary>
public class FixedSizeChunkingStrategy : IChunkingStrategy
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    public string StrategyName => "fixed-size";

    public FixedSizeChunkingStrategy(int chunkSize = 80, int overlap = 15)
    {
        if (chunkSize <= 0)   throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (overlap  <  0)   throw new ArgumentOutOfRangeException(nameof(overlap));
        if (overlap  >= chunkSize) throw new ArgumentException("Overlap must be smaller than chunk size.");

        _chunkSize = chunkSize;
        _overlap   = overlap;
    }

    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ParsedPage> pages)
    {
        // Flatten all pages into one word list, keeping track of which page each word came from.
        // This lets overlap cross page boundaries naturally (a chunk near the bottom of page 3
        // can include a few words from page 4) while still citing the page where the chunk started.
        var words     = new List<(string Word, int PageNumber)>();

        foreach (var page in pages)
        {
            var pageWords = page.Text.Split(
                [' ', '\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var w in pageWords)
                words.Add((w, page.PageNumber));
        }

        var chunks = new List<TextChunk>();
        int step   = _chunkSize - _overlap;
        int start  = 0;
        int ordinal = 0;

        while (start < words.Count)
        {
            int end        = Math.Min(start + _chunkSize, words.Count);
            int pageNumber = words[start].PageNumber;   // cite the page where the chunk starts
            string text    = string.Join(" ", words[start..end].Select(w => w.Word));

            chunks.Add(new TextChunk(ordinal++, pageNumber, text));

            if (end == words.Count) break;
            start += step;
        }

        return chunks;
    }
}
