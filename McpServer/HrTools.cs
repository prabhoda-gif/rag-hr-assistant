using System.ComponentModel;
using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using ModelContextProtocol.Server;
using OpenAI;
using OpenAI.Embeddings;

namespace McpServer;

// =============================================================================
// HrTools — MCP tool implementations
//
// LEARNING NOTE — MCP Tool Definition
// Each static method decorated with [McpServerTool] becomes a tool that any
// MCP client can discover and call. The [Description] attributes are read by
// the MCP protocol and sent to the AI so it knows when to use each tool.
//
// Compare this to our AgentService where we manually built ChatTool objects
// with JSON schemas. MCP does all that automatically from the method signature
// and Description attributes — much less boilerplate.
//
// LEARNING NOTE — [McpServerToolType]
// This attribute marks the class as a container of MCP tools.
// The MCP SDK scans for all static methods with [McpServerTool] inside it.
// =============================================================================

[McpServerToolType]
public static class HrTools
{
    // ── Injected via DI — set once at startup ─────────────────────────────────
    public static string ConnectionString { get; set; } = "";
    public static string OpenAiApiKey     { get; set; } = "";

    // =========================================================================
    // Tool 1: Search HR Documents
    // =========================================================================
    [McpServerTool, Description(
        "Search the company HR policy documents. Use this for questions about " +
        "leave policies, benefits, procedures, rules, and any HR-related topic.")]
    public static async Task<string> SearchHrDocs(
        [Description("The search query describing what HR information to find")]
        string query,
        [Description("The user ID of the employee asking the question")]
        string userId = "public")
    {
        try
        {
            // Step 1: embed the query using OpenAI
            var embeddingClient = new OpenAIClient(OpenAiApiKey)
                .GetEmbeddingClient("text-embedding-3-small");

            var embResult = await embeddingClient.GenerateEmbeddingAsync(query);
            float[] vector = embResult.Value.ToFloats().ToArray();

            // Step 2: extract keywords for LIKE search
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "what", "when", "where", "which", "who", "how", "does", "with",
                  "from", "that", "this", "have", "will", "are", "the", "and",
                  "for", "not", "can", "get", "its", "per" };

            var keywords = query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', '?', '!', ',').ToLowerInvariant())
                .Where(w => w.Length > 3 && !stopWords.Contains(w))
                .Distinct()
                .Take(5)
                .ToList();

            // Step 3: run vector search
            var vectorResults  = await RunVectorSearchAsync(vector, userId);
            var keywordResults = keywords.Count > 0
                ? await RunKeywordSearchAsync(keywords, userId)
                : new List<ChunkRow>();

            // Step 4: RRF fusion
            var scores = new Dictionary<int, (ChunkRow Chunk, double Score)>();

            for (int i = 0; i < vectorResults.Count; i++)
            {
                var chunk = vectorResults[i];
                double score = 1.0 / (i + 1 + 60);
                if (scores.TryGetValue(chunk.ChunkIndex, out var ex))
                    scores[chunk.ChunkIndex] = (ex.Chunk, ex.Score + score);
                else
                    scores[chunk.ChunkIndex] = (chunk, score);
            }

            for (int i = 0; i < keywordResults.Count; i++)
            {
                var chunk = keywordResults[i];
                double score = 1.0 / (i + 1 + 60);
                if (scores.TryGetValue(chunk.ChunkIndex, out var ex))
                    scores[chunk.ChunkIndex] = (ex.Chunk, ex.Score + score);
                else
                    scores[chunk.ChunkIndex] = (chunk, score);
            }

            var top = scores.Values.OrderByDescending(x => x.Score).Take(5).ToList();

            if (top.Count == 0)
                return "No relevant HR documents found for this query.";

            var sb = new System.Text.StringBuilder();
            foreach (var (chunk, _) in top)
            {
                sb.AppendLine($"[Source: {chunk.FileName}, chunk {chunk.ChunkIndex}]");
                sb.AppendLine(chunk.ChunkText);
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Search failed: {ex.Message}";
        }
    }

    // =========================================================================
    // Tool 2: Get Leave Balance
    // =========================================================================
    [McpServerTool, Description(
        "Get the remaining leave balance for an employee — annual, sick and casual days left.")]
    public static Task<string> GetLeaveBalance(
        [Description("The employee's user ID")]
        string userId)
    {
        // Simulated data — in production this would query an HR system
        var balances = new Dictionary<string, (int Annual, int Sick, int Casual)>
        {
            ["alice"] = (12, 8, 3),
            ["bob"]   = (5, 10, 1),
            ["carol"] = (20, 12, 5),
        };

        if (balances.TryGetValue(userId.ToLowerInvariant(), out var b))
        {
            return Task.FromResult(
                $"Leave balance for {userId}:\n" +
                $"  Annual leave:  {b.Annual} days remaining\n" +
                $"  Sick leave:    {b.Sick} days remaining\n" +
                $"  Casual leave:  {b.Casual} days remaining");
        }

        return Task.FromResult(
            $"No record found for '{userId}'. Default balance: 15 annual, 10 sick, 5 casual days.");
    }

    // =========================================================================
    // Tool 3: Calculate Working Days
    // =========================================================================
    [McpServerTool, Description(
        "Calculate the number of working days (Mon–Fri, excluding weekends) between two dates. " +
        "Use this when an employee asks how many leave days a date range covers.")]
    public static Task<string> CalculateWorkingDays(
        [Description("Start date in YYYY-MM-DD format (inclusive)")]
        string startDate,
        [Description("End date in YYYY-MM-DD format (inclusive)")]
        string endDate)
    {
        if (!DateOnly.TryParse(startDate, out var start) ||
            !DateOnly.TryParse(endDate,   out var end))
            return Task.FromResult("Invalid date format. Please use YYYY-MM-DD.");

        if (end < start)
            return Task.FromResult("End date must be on or after start date.");

        int working = 0;
        var cur = start;
        while (cur <= end)
        {
            if (cur.DayOfWeek != DayOfWeek.Saturday &&
                cur.DayOfWeek != DayOfWeek.Sunday)
                working++;
            cur = cur.AddDays(1);
        }

        int total = end.DayNumber - start.DayNumber + 1;
        return Task.FromResult(
            $"From {start:dd MMM yyyy} to {end:dd MMM yyyy}:\n" +
            $"  Total calendar days: {total}\n" +
            $"  Working days (Mon-Fri): {working}\n" +
            $"  Weekend days: {total - working}");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<List<ChunkRow>> RunVectorSearchAsync(float[] vector, string userId)
    {
        const string sql = """
            SELECT TOP (20) FileName, ChunkIndex, ChunkText,
                VECTOR_DISTANCE('cosine', Embedding, @v) AS Dist
            FROM Documents
            WHERE UserId = @UserId OR UserId = 'public'
            ORDER BY Dist ASC
            """;

        var results = new List<ChunkRow>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add(new SqlParameter("@v", SqlDbTypeExtensions.Vector)
        {
            Value = new SqlVector<float>(vector.AsMemory())
        });

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            results.Add(new ChunkRow(r.GetString(0), r.GetInt32(1), r.GetString(2)));

        return results;
    }

    private static async Task<List<ChunkRow>> RunKeywordSearchAsync(
        List<string> keywords, string userId)
    {
        var cases  = keywords.Select((_, i) => $"CASE WHEN ChunkText LIKE @k{i} THEN 1 ELSE 0 END").ToList();
        var wheres = keywords.Select((_, i) => $"ChunkText LIKE @k{i}").ToList();

        var sql = $"""
            SELECT TOP (20) FileName, ChunkIndex, ChunkText
            FROM Documents
            WHERE (UserId = @UserId OR UserId = 'public')
              AND ({string.Join(" OR ", wheres)})
            ORDER BY ({string.Join(" + ", cases)}) DESC
            """;

        var results = new List<ChunkRow>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        for (int i = 0; i < keywords.Count; i++)
            cmd.Parameters.AddWithValue($"@k{i}", $"%{keywords[i]}%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            results.Add(new ChunkRow(r.GetString(0), r.GetInt32(1), r.GetString(2)));

        return results;
    }

    private record ChunkRow(string FileName, int ChunkIndex, string ChunkText);
}
