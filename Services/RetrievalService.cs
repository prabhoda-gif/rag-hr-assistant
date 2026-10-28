using Microsoft.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using RagApi.Models;

namespace RagApi.Services;

public interface IRetrievalService
{
    Task<List<ChunkResult>> RetrieveAsync(float[] questionVector, string userId);
}

public class RetrievalService : IRetrievalService
{
    private readonly string _connStr;
    private readonly int    _topK;
    private readonly double _threshold;

    public RetrievalService(IConfiguration config)
    {
        _connStr   = config.GetConnectionString("RAGDemo")
            ?? throw new InvalidOperationException("ConnectionStrings:RAGDemo not configured");
        _topK      = config.GetValue<int>("Rag:TopK", 5);
        _threshold = config.GetValue<double>("Rag:RelevanceThreshold", 0.5);
    }

    public async Task<List<ChunkResult>> RetrieveAsync(float[] questionVector, string userId)
    {
        // UserId filter means each user only sees their own documents.
        // 'public' is a special userId that everyone can see.
        const string sql = """
            SELECT TOP (@TopK)
                FileName, ChunkIndex, PageNumber, ChunkText,
                VECTOR_DISTANCE('cosine', @QuestionVector, Embedding) AS Distance
            FROM Documents
            WHERE UserId = @UserId OR UserId = 'public'
            ORDER BY Distance ASC
            """;

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TopK",   _topK);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add(new SqlParameter("@QuestionVector", SqlDbTypeExtensions.Vector)
        {
            Value = new SqlVector<float>(questionVector.AsMemory())
        });

        var results = new List<ChunkResult>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            double distance = reader.GetDouble(4);
            if (distance < _threshold)
            {
                results.Add(new ChunkResult
                {
                    FileName   = reader.GetString(0),
                    ChunkIndex = reader.GetInt32(1),
                    PageNumber = reader.GetInt32(2),
                    ChunkText  = reader.GetString(3),
                    Distance   = distance
                });
            }
        }

        return results;
    }
}
