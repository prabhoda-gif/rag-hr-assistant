using System.Text.RegularExpressions;
using RagApi.Services.Pdf;

namespace RagApi.Services.Chunking;

/// <summary>
/// Chunks text by accumulating complete sentences up to a word-count target.
///
/// LEARNING NOTE — Why sentence-aware chunking often beats fixed-size:
/// A fixed-size chunker might cut "Employees are entitled to 16 weeks of
/// maternity leave. The leave" — splitting the key fact across two chunks.
/// Sentence-aware chunking keeps sentences whole, so retrieval is more likely
/// to return a chunk that contains a complete, answerable statement.
///
/// When to prefer fixed-size:
/// - When text has no clear sentence boundaries (tables, bullet lists, code)
/// - When you need very precise token budget control
///
/// Sentence splitting uses a simple regex — it works well for clean prose but
/// will incorrectly split on abbreviations like "Dr.", "e.g.", "i.e."
/// A production system would use an NLP sentence tokenizer (e.g. NLP.NET).
/// </summary>
public class SentenceAwareChunkingStrategy : IChunkingStrategy
{
    // Splits after . ? ! followed by whitespace and an uppercase letter (or end of string)
    private static readonly Regex SentenceSplitter =
        new(@"(?<=[.?!])\s+(?=[A-Z])", RegexOptions.Compiled);

    private readonly int _targetWords;
    private readonly int _overlapSentences;

    public string StrategyName => "sentence-aware";

    public SentenceAwareChunkingStrategy(int targetWords = 100, int overlapSentences = 1)
    {
        _targetWords       = targetWords;
        _overlapSentences  = overlapSentences;
    }

    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ParsedPage> pages)
    {
        var chunks  = new List<TextChunk>();
        int ordinal = 0;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text)) continue;

            string[] sentences = SentenceSplitter.Split(page.Text.Trim());

            var   currentWords     = new List<string>();
            var   currentSentences = new List<string>();

            foreach (var sentence in sentences)
            {
                string trimmed = sentence.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string[] sentenceWords = trimmed.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries);

                // If adding this sentence would exceed target, emit the current chunk first
                if (currentWords.Count > 0 &&
                    currentWords.Count + sentenceWords.Length > _targetWords)
                {
                    chunks.Add(new TextChunk(ordinal++, page.PageNumber,
                        string.Join(" ", currentWords)));

                    // Keep the last N sentences as overlap for the next chunk
                    var overlapSentences = currentSentences
                        .TakeLast(_overlapSentences)
                        .ToList();

                    currentWords     = overlapSentences
                        .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .ToList();
                    currentSentences = overlapSentences;
                }

                currentSentences.Add(trimmed);
                currentWords.AddRange(sentenceWords);
            }

            // Emit the last chunk for this page
            if (currentWords.Count > 0)
                chunks.Add(new TextChunk(ordinal++, page.PageNumber,
                    string.Join(" ", currentWords)));
        }

        return chunks;
    }
}
