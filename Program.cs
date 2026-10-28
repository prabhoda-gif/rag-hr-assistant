using RagApi.Services;
using RagApi.Services.Chunking;
using RagApi.Services.Pdf;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase);

// ── Swagger (Development only) ─────────────────────────────────────────────
// LEARNING NOTE: Swagger gives you a browser UI to test every endpoint
// without needing a REST client. Visit /swagger when running locally.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "RAG HR Assistant API",
        Version     = "v1",
        Description = "Retrieval-Augmented Generation API — learning project"
    });
});

// ── PDF Parser ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IPdfParser, PdfPigParser>();

// ── Chunking Strategy (selected by config) ────────────────────────────────
// LEARNING NOTE: This is the Strategy pattern. You can change chunking
// behaviour entirely by editing appsettings.json — no code change needed.
// Try "fixed" vs "sentence" and observe the difference in your eval results.
string chunkingStrategy = builder.Configuration
    .GetValue<string>("Rag:ChunkingStrategy", "fixed")!
    .ToLowerInvariant();

if (chunkingStrategy == "sentence")
{
    int targetWords      = builder.Configuration.GetValue<int>("Rag:ChunkSize",    100);
    int overlapSentences = builder.Configuration.GetValue<int>("Rag:ChunkOverlap", 1);
    builder.Services.AddSingleton<IChunkingStrategy>(
        new SentenceAwareChunkingStrategy(targetWords, overlapSentences));
}
else
{
    int chunkSize    = builder.Configuration.GetValue<int>("Rag:ChunkSize",    80);
    int chunkOverlap = builder.Configuration.GetValue<int>("Rag:ChunkOverlap", 15);
    builder.Services.AddSingleton<IChunkingStrategy>(
        new FixedSizeChunkingStrategy(chunkSize, chunkOverlap));
}

// ── Retrieval Strategy (selected by config) ───────────────────────────────
// LEARNING NOTE: This lets you switch between pure vector search and hybrid
// retrieval (vector + Full-Text keyword search fused with RRF) via config.
// Set Rag:RetrievalStrategy = "hybrid" in appsettings.json to enable.
// Prerequisite for hybrid: run sql/migration_002_fulltext_index.sql first.
string retrievalStrategy = builder.Configuration
    .GetValue<string>("Rag:RetrievalStrategy", "vector")!
    .ToLowerInvariant();

if (retrievalStrategy == "hybrid")
    builder.Services.AddSingleton<IRetrievalService, HybridRetrievalService>();
else
    builder.Services.AddSingleton<IRetrievalService, RetrievalService>();

// ── Core RAG Services ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IEmbeddingService,  EmbeddingService>();
builder.Services.AddSingleton<IIngestionService,  IngestionService>();
builder.Services.AddSingleton<IAnswerService,     AnswerService>();
builder.Services.AddSingleton<IConversationStore, ConversationStore>();

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RAG HR Assistant v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseDefaultFiles();   // serves wwwroot/index.html at /
app.UseStaticFiles();
app.MapControllers();

app.Run();
