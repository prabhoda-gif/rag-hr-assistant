using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;
using OpenAI;

namespace RagApi.Services;

// =============================================================================
// SummarizationService — GenAI document summarization
//
// LEARNING NOTE — How is this different from Q&A?
//
// Q&A flow:    question → embed → vector search → TOP 5 chunks → GPT answers
// Summary flow: fileName → SELECT ALL chunks → GPT summarizes (no search)
//
// In Q&A we want the MOST RELEVANT chunks (retrieval problem).
// In summarization we want EVERY chunk (we need the full picture).
//
// LEARNING NOTE — Token limits and map-reduce
// GPT-4o-mini supports 128,000 tokens (~96,000 words). For small documents
// (like our HR handbook) all chunks fit in one call — "single-pass" summarization.
//
// For large documents (annual reports, legal contracts) you would use:
//   Map step:    split chunks into batches → summarize each batch separately
//   Reduce step: combine all mini-summaries → one final summary
// This is called "map-reduce summarization" and is a standard LLM pattern.
// =============================================================================

public interface ISummarizationService
{
    Task<string> SummarizeAsync(string fileName, string userId, CancellationToken ct = default);
    IAsyncEnumerable<string> SummarizeStreamingAsync(string fileName, string userId, CancellationToken ct = default);
}

public class SummarizationService : ISummarizationService
{
    private readonly string      _connStr;
    private readonly IChatClient _chatClient;

    public SummarizationService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("RAGDemo")
            ?? throw new InvalidOperationException("ConnectionStrings:RAGDemo not configured");

        string apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

        _chatClient = new OpenAIClient(apiKey)
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient();
    }

    // -------------------------------------------------------------------------
    // Non-streaming version (simple, blocking)
    // -------------------------------------------------------------------------
    public async Task<string> SummarizeAsync(
        string fileName, string userId, CancellationToken ct = default)
    {
        var chunks = await FetchAllChunksAsync(fileName, userId, ct);

        if (chunks.Count == 0)
            return "No content found for this document.";

        var messages = BuildSummarizationMessages(fileName, chunks);
        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    // -------------------------------------------------------------------------
    // Streaming version — sends tokens as they arrive (better UX)
    // -------------------------------------------------------------------------
    public async IAsyncEnumerable<string> SummarizeStreamingAsync(
        string fileName, string userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunks = await FetchAllChunksAsync(fileName, userId, ct);

        if (chunks.Count == 0)
        {
            yield return "No content found for this document.";
            yield break;
        }

        var messages = BuildSummarizationMessages(fileName, chunks);

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is not null)
                yield return update.Text;
        }
    }

    // -------------------------------------------------------------------------
    // Fetch ALL chunks for a document — ordered by ChunkIndex so text is in order
    //
    // LEARNING NOTE — Why ORDER BY ChunkIndex?
    // Chunks were created in document order (page 1 → page 2 → ...).
    // If we feed them out of order, the LLM sees a jumbled document.
    // ORDER BY ChunkIndex preserves the original reading order.
    // -------------------------------------------------------------------------
    private async Task<List<string>> FetchAllChunksAsync(
        string fileName, string userId, CancellationToken ct)
    {
        const string sql = """
            SELECT ChunkText
            FROM   Documents
            WHERE  FileName = @FileName
              AND  (UserId = @UserId OR UserId = 'public')
            ORDER  BY ChunkIndex
            """;

        var chunks = new List<string>();

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@FileName", fileName);
        cmd.Parameters.AddWithValue("@UserId",   userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            chunks.Add(reader.GetString(0));

        return chunks;
    }

    // -------------------------------------------------------------------------
    // Build the prompt for summarization
    //
    // LEARNING NOTE — Prompt engineering for summarization
    // The system prompt tells the LLM:
    //   1. Its role (document analyst)
    //   2. The desired output FORMAT (bullet points by section)
    //   3. What NOT to do (don't hallucinate, stick to the document)
    //
    // Giving a specific output format in the system prompt dramatically
    // improves consistency — the LLM won't invent its own structure.
    // -------------------------------------------------------------------------
    private static List<ChatMessage> BuildSummarizationMessages(
        string fileName, List<string> chunks)
    {
        // Join all chunks into one document text, separated by markers
        var documentText = new System.Text.StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            documentText.AppendLine($"[Chunk {i + 1}]");
            documentText.AppendLine(chunks[i]);
            documentText.AppendLine();
        }

        string systemPrompt = $"""
            You are a professional document analyst. Your job is to produce a
            clear, structured summary of the document provided.

            Output format — use exactly this structure:
            ## Document Summary: {fileName}

            **Key Topics Covered:**
            • [topic 1]
            • [topic 2]
            (list all main topics)

            **Section Summaries:**
            For each section found in the document:
            ### [Section Name]
            • [key point 1]
            • [key point 2]
            • [key point 3]

            **Important Numbers & Dates:**
            • [any specific numbers, days, weeks, percentages mentioned]

            Rules:
            - Only summarize what is actually in the document
            - Do not add information not present in the text
            - Keep bullet points concise (one line each)
            - If a section has sub-rules or conditions, include them as sub-bullets
            """;

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User,
                $"Please summarize the following document:\n\n{documentText}")
        ];
    }
}
