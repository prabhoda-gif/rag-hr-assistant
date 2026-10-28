using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using RagApi.Services.Chunking;
using RagApi.Services.Pdf;

namespace RagApi.Services;

public interface IIngestionService
{
    Task<int> IngestAsync(string fileName, Stream fileStream, string userId,
        CancellationToken ct = default);
}

public class IngestionService : IIngestionService
{
    private readonly IEmbeddingService  _embedder;
    private readonly IPdfParser         _pdfParser;
    private readonly IChunkingStrategy  _chunker;
    private readonly string             _connStr;
    private readonly int                _batchSize;

    public IngestionService(
        IEmbeddingService  embedder,
        IPdfParser         pdfParser,
        IChunkingStrategy  chunker,
        IConfiguration     config)
    {
        _embedder  = embedder;
        _pdfParser = pdfParser;
        _chunker   = chunker;
        _connStr   = config.GetConnectionString("RAGDemo")
            ?? throw new InvalidOperationException("ConnectionStrings:RAGDemo not configured");
        _batchSize = config.GetValue<int>("Rag:BatchSize", 20);
    }

    public async Task<int> IngestAsync(string fileName, Stream fileStream,
        string userId, CancellationToken ct = default)
    {
        // --- 1. Parse PDF into pages ----------------------------------------
        var pages  = _pdfParser.Parse(fileStream);

        // --- 2. Chunk pages using configured strategy -----------------------
        var chunks = _chunker.Chunk(pages);

        if (chunks.Count == 0)
            throw new InvalidOperationException(
                "No text could be extracted from the document. " +
                "The PDF may be scanned or image-only — OCR is not supported.");

        // --- 3. Persist to SQL Server in batches ----------------------------
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        // Delete any existing chunks for this file owned by this user
        // so re-ingestion is safe and idempotent.
        await using (var del = new SqlCommand(
            "DELETE FROM Documents WHERE FileName = @FileName AND UserId = @UserId", conn))
        {
            del.Parameters.AddWithValue("@FileName", fileName);
            del.Parameters.AddWithValue("@UserId",   userId);
            await del.ExecuteNonQueryAsync(ct);
        }

        int inserted     = 0;
        int totalBatches = (int)Math.Ceiling(chunks.Count / (double)_batchSize);

        for (int b = 0; b < totalBatches; b++)
        {
            ct.ThrowIfCancellationRequested();

            var batch      = chunks.Skip(b * _batchSize).Take(_batchSize).ToList();
            var texts      = batch.Select(c => c.Text).ToList();
            var embeddings = await _embedder.EmbedBatchAsync(texts);

            foreach (var (chunk, idx) in batch.Select((c, i) => (c, i)))
            {
                float[] vector = embeddings[idx];

                const string sql = """
                    INSERT INTO Documents (FileName, ChunkIndex, PageNumber, ChunkText, Embedding, UserId)
                    VALUES (@FileName, @ChunkIndex, @PageNumber, @ChunkText, @Embedding, @UserId)
                    """;

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@FileName",   fileName);
                cmd.Parameters.AddWithValue("@ChunkIndex", chunk.Ordinal);
                cmd.Parameters.AddWithValue("@PageNumber", chunk.PageNumber);
                cmd.Parameters.AddWithValue("@ChunkText",  chunk.Text);
                cmd.Parameters.AddWithValue("@UserId",     userId);
                cmd.Parameters.Add(new SqlParameter("@Embedding", SqlDbTypeExtensions.Vector)
                {
                    Value = new SqlVector<float>(vector.AsMemory())
                });

                await cmd.ExecuteNonQueryAsync(ct);
                inserted++;
            }
        }

        return inserted;
    }
}
