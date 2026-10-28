using RagApi.Services.Pdf;

namespace RagApi.Services.Chunking;

/// <summary>
/// A single chunk of text ready for embedding.
/// Carries its source page so every inserted row has a real citation.
/// </summary>
public record TextChunk(int Ordinal, int PageNumber, string Text);

/// <summary>
/// Contract for all chunking strategies.
///
/// LEARNING NOTE — Why an interface?
/// Swapping chunking strategy is one of the most impactful RAG experiments.
/// Hiding it behind an interface lets you change the strategy in config
/// without touching ingestion logic, and lets you unit-test chunking
/// independently of any database or LLM calls.
/// </summary>
public interface IChunkingStrategy
{
    string StrategyName { get; }

    /// <summary>
    /// Splits a list of parsed pages into chunks ready for embedding.
    /// </summary>
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ParsedPage> pages);
}
