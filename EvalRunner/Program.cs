// =============================================================================
// EvalRunner — RAG Evaluation Harness
//
// LEARNING NOTE — What is this?
// This console app measures how good your RAG retrieval is by running a set
// of known questions against the real database and checking whether the correct
// chunk appears in the top K results.
//
// Key metric: Recall@k
//   For each question, we check: "Is the answer somewhere in the top K chunks?"
//   YES = pass, NO = fail
//   Recall@k = (number of passes) / (total questions)
//
// Why Recall and not Precision?
//   We pass the top-k chunks to the LLM. If the right chunk isn't in there
//   at all, the LLM cannot possibly answer correctly — so finding it matters
//   more than how many of the k chunks are "good".
//
// How to run:
//   cd EvalRunner
//   dotnet run -- --openai-key sk-xxx --conn "Data Source=localhost;..."
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using OpenAI;
using OpenAI.Embeddings;

// ── 1. Parse arguments ───────────────────────────────────────────────────────
string openAiKey = GetArg(args, "--openai-key")
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new Exception("Pass --openai-key or set OPENAI_API_KEY env var.");

string connString = GetArg(args, "--conn")
    ?? Environment.GetEnvironmentVariable("SQL_CONNECTION")
    ?? throw new Exception("Pass --conn or set SQL_CONNECTION env var.");

string questionsFile = GetArg(args, "--questions")
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Eval", "eval_questions.jsonl");

Console.WriteLine("===========================================");
Console.WriteLine("  RAG Eval Harness — Recall@k Measurement");
Console.WriteLine("===========================================");
Console.WriteLine($"Questions file : {Path.GetFullPath(questionsFile)}");
Console.WriteLine($"Evaluating     : Recall@1, Recall@3, Recall@5");
Console.WriteLine();

// ── 2. Load questions ────────────────────────────────────────────────────────
if (!File.Exists(questionsFile))
    throw new FileNotFoundException($"Questions file not found: {questionsFile}");

var questions = File.ReadAllLines(questionsFile)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Select(line => JsonSerializer.Deserialize<EvalQuestion>(line)!)
    .ToList();

Console.WriteLine($"Loaded {questions.Count} questions.\n");

// ── 3. Create OpenAI embedding client ────────────────────────────────────────
//
// LEARNING NOTE — Why do we need embeddings here?
// Retrieval works by: question → embedding vector → cosine similarity in SQL.
// The eval harness must do the same so it tests the REAL pipeline end-to-end.
//
var openAiClient = new OpenAIClient(openAiKey);
var embedder = openAiClient.GetEmbeddingClient("text-embedding-3-small");

// ── 4. Run eval for each question ─────────────────────────────────────────────
var results = new List<EvalResult>();

foreach (var q in questions)
{
    Console.Write($"[Q{q.Id:D2}] {q.Question[..Math.Min(60, q.Question.Length)]}...");

    // Embed the question — same as the real API does
    var embeddingResult = await embedder.GenerateEmbeddingAsync(q.Question);
    float[] vector = embeddingResult.Value.ToFloats().ToArray();

    // Retrieve top 5 chunks from SQL using VECTOR_DISTANCE (cosine similarity)
    var chunks = await RetrieveTopChunksAsync(connString, vector, q.SourceFile, topK: 5);

    // Score at k=1, k=3, k=5
    bool passAt1 = ChunkContainsKeywords(chunks.Take(1), q.ExpectedKeywords);
    bool passAt3 = ChunkContainsKeywords(chunks.Take(3), q.ExpectedKeywords);
    bool passAt5 = ChunkContainsKeywords(chunks.Take(5), q.ExpectedKeywords);

    results.Add(new EvalResult(q, chunks, passAt1, passAt3, passAt5));

    string status = passAt3 ? "✓ PASS" : "✗ FAIL";
    Console.WriteLine($" {status} (Recall@3)");

    if (!passAt3)
    {
        Console.WriteLine($"         Expected : {string.Join(", ", q.ExpectedKeywords)}");
        Console.WriteLine($"         Top chunk: {chunks.FirstOrDefault()?[..Math.Min(80, chunks.FirstOrDefault()?.Length ?? 0)] ?? "(no chunks retrieved)"}");
    }
}

// ── 5. Print summary ──────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("===========================================");
Console.WriteLine("  RESULTS SUMMARY");
Console.WriteLine("===========================================");

double recall1 = results.Count(r => r.PassAt1) / (double)results.Count * 100;
double recall3 = results.Count(r => r.PassAt3) / (double)results.Count * 100;
double recall5 = results.Count(r => r.PassAt5) / (double)results.Count * 100;

Console.WriteLine($"  Recall@1 : {recall1:F0}%  ({results.Count(r => r.PassAt1)}/{results.Count})");
Console.WriteLine($"  Recall@3 : {recall3:F0}%  ({results.Count(r => r.PassAt3)}/{results.Count})");
Console.WriteLine($"  Recall@5 : {recall5:F0}%  ({results.Count(r => r.PassAt5)}/{results.Count})");
Console.WriteLine();

var failed = results.Where(r => !r.PassAt3).ToList();
if (failed.Any())
{
    Console.WriteLine($"  Failed at Recall@3 ({failed.Count} questions):");
    foreach (var f in failed)
        Console.WriteLine($"    - Q{f.Question.Id}: {f.Question.Question}");
}
else
{
    Console.WriteLine("  All questions passed Recall@3!");
}

Console.WriteLine("===========================================");

// ── 6. Save results to JSON ───────────────────────────────────────────────────
var outputPath = Path.Combine(AppContext.BaseDirectory, "eval_results.json");
var output = new
{
    RunAt   = DateTime.UtcNow,
    Recall1 = recall1,
    Recall3 = recall3,
    Recall5 = recall5,
    Details = results.Select(r => new
    {
        r.Question.Id,
        r.Question.Question,
        r.PassAt1,
        r.PassAt3,
        r.PassAt5,
        TopChunkSnippet = r.RetrievedChunks
            .FirstOrDefault()?[..Math.Min(120, r.RetrievedChunks.FirstOrDefault()?.Length ?? 0)]
    })
};

await File.WriteAllTextAsync(outputPath,
    JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"\nFull results saved to: {outputPath}");

// =============================================================================
// Helper: retrieve top-k chunks from SQL using vector similarity
// =============================================================================
static async Task<List<string>> RetrieveTopChunksAsync(
    string connString, float[] vector, string fileName, int topK)
{
    // LEARNING NOTE — VECTOR_DISTANCE is SQL Server 2025's native cosine similarity.
    // Lower distance = more similar. ORDER BY ASC = best match first.
    const string sql = """
        SELECT TOP (@topK) ChunkText
        FROM   Documents
        WHERE  FileName = @fileName
        ORDER  BY VECTOR_DISTANCE('cosine', Embedding, CAST(@vec AS VECTOR(1536)))
        """;

    var chunks = new List<string>();

    await using var conn = new SqlConnection(connString);
    await conn.OpenAsync();
    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@topK",     topK);
    cmd.Parameters.AddWithValue("@fileName", fileName);
    cmd.Parameters.AddWithValue("@vec",      "[" + string.Join(",", vector) + "]");

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        chunks.Add(reader.GetString(0));

    return chunks;
}

// =============================================================================
// Helper: check if any retrieved chunk contains ALL expected keywords
// =============================================================================
static bool ChunkContainsKeywords(IEnumerable<string> chunks, string[] keywords)
{
    // LEARNING NOTE — We use keyword matching (not exact strings) because chunks
    // may have extra whitespace. A production eval would use an LLM-as-judge
    // for semantic correctness instead of lexical keyword matching.
    foreach (var chunk in chunks)
    {
        bool allFound = keywords.All(kw =>
            chunk.Contains(kw, StringComparison.OrdinalIgnoreCase));
        if (allFound) return true;
    }
    return false;
}

// =============================================================================
// Helper: parse named CLI argument  e.g. --openai-key sk-xxx
// =============================================================================
static string? GetArg(string[] args, string name)
{
    int idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

// =============================================================================
// Models
// =============================================================================
record EvalQuestion(
    [property: JsonPropertyName("id")]                int      Id,
    [property: JsonPropertyName("question")]          string   Question,
    [property: JsonPropertyName("expected_keywords")] string[] ExpectedKeywords,
    [property: JsonPropertyName("source_file")]       string   SourceFile
);

record EvalResult(
    EvalQuestion Question,
    List<string> RetrievedChunks,
    bool         PassAt1,
    bool         PassAt3,
    bool         PassAt5
);
