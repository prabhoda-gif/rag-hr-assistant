using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using RagApi.Models;

namespace RagApi.Services;

// =============================================================================
// HybridRetrievalService — combines vector search + keyword search using RRF
//
// LEARNING NOTE — Why Hybrid?
// Pure vector search is great at semantic similarity but struggles with:
//   - Exact terms: "PF", "ESIC", "Clause 4.2"
//   - Numbers: "21 days", "8 weeks"
//   - Acronyms and codes users type exactly as they appear in the document
//
// Pure keyword search is great at exact matches but misses:
//   - Synonyms: "annual leave" vs "vacation days"
//   - Paraphrasing: "how long is maternity leave" vs "parental leave duration"
//
// Hybrid retrieval gets the best of both worlds.
//
// LEARNING NOTE — RRF (Reciprocal Rank Fusion)
// RRF is a simple, parameter-free way to merge two ranked lists.
// Formula: score = 1/(rank + k)  where k=60 is a standard smoothing constant.
//
// Example:
//   Vector rank:  ChunkA=#1, ChunkB=#3
//   Keyword rank: ChunkB=#1, ChunkA=#4
//
//   ChunkA RRF = 1/(1+60) + 1/(4+60) = 0.0164 + 0.0156 = 0.0320
//   ChunkB RRF = 1/(3+60) + 1/(1+60) = 0.0159 + 0.0164 = 0.0323
//   → ChunkB wins (slightly higher combined score)
//
// k=60 prevents a rank-1 result from completely dominating.
// =============================================================================

public class HybridRetrievalService : IRetrievalService
{
    private readonly string _connStr;
    private readonly int    _topK;
    private const    int    RrfK = 60;   // standard RRF smoothing constant

    public HybridRetrievalService(IConfiguration config)
    {
        _connStr = config.GetConnectionString("RAGDemo")
            ?? throw new InvalidOperationException("ConnectionStrings:RAGDemo not configured");
        _topK = config.GetValue<int>("Rag:TopK", 5);
    }

    public async Task<List<ChunkResult>> RetrieveAsync(float[] questionVector, string userId)
    {
        // Run both searches in parallel — they are independent SQL queries
        var vectorTask  = RunVectorSearchAsync(questionVector, userId);
        var keywordTask = RunKeywordSearchAsync(questionVector, userId);

        await Task.WhenAll(vectorTask, keywordTask);

        var vectorResults  = vectorTask.Result;   // ranked by cosine distance
        var keywordResults = keywordTask.Result;  // ranked by FTS rank

        // LEARNING NOTE — RRF Fusion
        // We build a dictionary keyed by ChunkIndex to accumulate scores.
        // Each list contributes 1/(rank + RrfK) to the chunk's total score.
        // Higher total score = better combined rank.
        var scores = new Dictionary<int, (ChunkResult Chunk, double Score)>();

        // Add vector search scores (rank 1, 2, 3 ...)
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var chunk = vectorResults[i];
            double rrfScore = 1.0 / (i + 1 + RrfK);

            if (scores.TryGetValue(chunk.ChunkIndex, out var existing))
                scores[chunk.ChunkIndex] = (existing.Chunk, existing.Score + rrfScore);
            else
                scores[chunk.ChunkIndex] = (chunk, rrfScore);
        }

        // Add keyword search scores (rank 1, 2, 3 ...)
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var chunk = keywordResults[i];
            double rrfScore = 1.0 / (i + 1 + RrfK);

            if (scores.TryGetValue(chunk.ChunkIndex, out var existing))
                scores[chunk.ChunkIndex] = (existing.Chunk, existing.Score + rrfScore);
            else
                scores[chunk.ChunkIndex] = (chunk, rrfScore);
        }

        // Sort by RRF score descending, take top K
        // Store the RRF score in Distance field (lower = better in vector land,
        // but here higher = better — we negate it so the UI still makes sense)
        return scores.Values
            .OrderByDescending(x => x.Score)
            .Take(_topK)
            .Select(x =>
            {
                x.Chunk.Distance = 1.0 - x.Score * 100; // normalise for display
                return x.Chunk;
            })
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Vector search — same SQL as the original RetrievalService
    // Returns top 20 candidates (wider net for RRF to work with)
    // -------------------------------------------------------------------------
    private async Task<List<ChunkResult>> RunVectorSearchAsync(
        float[] questionVector, string userId)
    {
        const string sql = """
            SELECT TOP (20)
                FileName, ChunkIndex, PageNumber, ChunkText,
                VECTOR_DISTANCE('cosine', Embedding, @QuestionVector) AS Distance
            FROM Documents
            WHERE UserId = @UserId OR UserId = 'public'
            ORDER BY Distance ASC
            """;

        return await ExecuteChunkQueryAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.Add(new SqlParameter("@QuestionVector", SqlDbTypeExtensions.Vector)
            {
                Value = new SqlVector<float>(questionVector.AsMemory())
            });
        });
    }

    // -------------------------------------------------------------------------
    // Keyword search — uses LIKE-based matching on significant words
    //
    // LEARNING NOTE — Why not FREETEXT / CONTAINS?
    // Those require SQL Server Full-Text Search feature to be installed.
    // LIKE '%word%' works without any extra SQL feature, making this approach
    // portable and simple — at the cost of:
    //   - No stemming (won't match "leave" when user types "leaves")
    //   - Full table scan (slower on millions of rows)
    //
    // In production you would install Full-Text Search and swap LIKE for
    // FREETEXT — the RRF fusion logic above stays identical.
    //
    // LIKE keyword strategy:
    //   Extract words > 3 chars from the question, skip stop words,
    //   build: ChunkText LIKE '%word1%' OR ChunkText LIKE '%word2%'
    //   Rank by how many keywords matched (COUNT of CASE expressions)
    // -------------------------------------------------------------------------
    private async Task<List<ChunkResult>> RunKeywordSearchAsync(
        float[] questionVector, string userId)
    {
        var question = _pendingQuestion ?? string.Empty;

        if (string.IsNullOrWhiteSpace(question))
            return [];

        // Extract meaningful keywords — skip short words and common stop words
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "what", "when", "where", "which", "who", "how", "does", "with",
              "from", "that", "this", "have", "will", "are", "the", "and",
              "for", "not", "can", "get", "its", "per" };

        var keywords = question
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', '?', '!', ',').ToLowerInvariant())
            .Where(w => w.Length > 3 && !stopWords.Contains(w))
            .Distinct()
            .Take(5)   // cap at 5 keywords to keep query manageable
            .ToList();

        if (keywords.Count == 0)
            return [];

        // Build: CASE WHEN ChunkText LIKE '%kw1%' THEN 1 ELSE 0 END + ... AS MatchScore
        // WHERE at least one keyword matches
        var matchCases  = keywords.Select((kw, i) =>
            $"CASE WHEN ChunkText LIKE @kw{i} THEN 1 ELSE 0 END").ToList();
        var whereClause = keywords.Select((_, i) =>
            $"ChunkText LIKE @kw{i}").ToList();

        var sql = $"""
            SELECT TOP (20)
                FileName, ChunkIndex, PageNumber, ChunkText,
                CAST(0.0 AS FLOAT) AS Distance
            FROM Documents
            WHERE (UserId = @UserId OR UserId = 'public')
              AND ({string.Join(" OR ", whereClause)})
            ORDER BY ({string.Join(" + ", matchCases)}) DESC
            """;

        return await ExecuteChunkQueryAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("@UserId", userId);
            for (int i = 0; i < keywords.Count; i++)
                cmd.Parameters.AddWithValue($"@kw{i}", $"%{keywords[i]}%");
        });
    }

    // -------------------------------------------------------------------------
    // Shared reader — maps SqlDataReader rows to ChunkResult objects
    // -------------------------------------------------------------------------
    private async Task<List<ChunkResult>> ExecuteChunkQueryAsync(
        string sql, Action<SqlCommand> parameterize)
    {
        var results = new List<ChunkResult>();

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        parameterize(cmd);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ChunkResult
            {
                FileName   = reader.GetString(0),
                ChunkIndex = reader.GetInt32(1),
                PageNumber = reader.GetInt32(2),
                ChunkText  = reader.GetString(3),
                Distance   = reader.GetDouble(4)
            });
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // The question text needed for FREETEXT — set by AskController before
    // calling RetrieveAsync. This is a known design smell: the interface
    // IRetrievalService.RetrieveAsync only accepts a vector, not the question
    // string. In a real refactor we would add question to the interface.
    // -------------------------------------------------------------------------
    [ThreadStatic]
    private static string? _pendingQuestion;

    public void SetQuestion(string question) => _pendingQuestion = question;
}
