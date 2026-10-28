using RagApi.Models;

namespace RagApi.Services.Agent;

// =============================================================================
// SearchHrDocsTool — wraps the existing hybrid retrieval pipeline
//
// LEARNING NOTE — Tool Reuse
// We don't rewrite retrieval logic here. The agent tool is a thin wrapper that:
//   1. Takes a plain string query from the LLM
//   2. Calls EmbeddingService to get a vector
//   3. Calls HybridRetrievalService (vector + keyword + RRF)
//   4. Returns the top chunks as formatted text the LLM can read
//
// This is the key architecture win: our existing RAG pipeline becomes
// a tool that an intelligent agent can choose to invoke.
// =============================================================================

public class SearchHrDocsTool
{
    private readonly IEmbeddingService  _embedding;
    private readonly IRetrievalService  _retrieval;

    public SearchHrDocsTool(IEmbeddingService embedding, IRetrievalService retrieval)
    {
        _embedding = embedding;
        _retrieval = retrieval;
    }

    public static AgentTool Definition => new()
    {
        Name        = "search_hr_docs",
        Description = "Search the HR policy documents for information about leave, benefits, policies, rules and procedures. Use this when the user asks about company HR policies.",
        Parameters  = new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type        = "string",
                    description = "The search query — a clear, concise phrase describing what to look for"
                }
            },
            required = new[] { "query" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(string query, string userId)
    {
        try
        {
            // Set question for keyword search (hybrid retrieval needs the raw text)
            if (_retrieval is HybridRetrievalService hybrid)
                hybrid.SetQuestion(query);

            var vector = await _embedding.EmbedAsync(query);
            var chunks  = await _retrieval.RetrieveAsync(vector, userId);

            if (chunks.Count == 0)
                return new ToolResult { ToolName = "search_hr_docs", Output = "No relevant documents found." };

            // Format chunks as readable text for the LLM
            var sb = new System.Text.StringBuilder();
            foreach (var chunk in chunks)
            {
                sb.AppendLine($"[Source: {chunk.FileName}, chunk {chunk.ChunkIndex}]");
                sb.AppendLine(chunk.ChunkText);
                sb.AppendLine();
            }

            return new ToolResult { ToolName = "search_hr_docs", Output = sb.ToString() };
        }
        catch (Exception ex)
        {
            return new ToolResult { ToolName = "search_hr_docs", Output = $"Search failed: {ex.Message}", IsError = true };
        }
    }
}
