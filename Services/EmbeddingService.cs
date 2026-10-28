using Microsoft.Extensions.AI;
using OpenAI;

namespace RagApi.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text);
    Task<List<float[]>> EmbedBatchAsync(List<string> texts);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public EmbeddingService(IConfiguration config)
    {
        string apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

        _generator = new OpenAIClient(apiKey)
            .GetEmbeddingClient("text-embedding-3-small")
            .AsIEmbeddingGenerator();
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        var result = await _generator.GenerateAsync([text]);
        return result[0].Vector.ToArray();
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> texts)
    {
        var result = await _generator.GenerateAsync(texts);
        return result.Select(e => e.Vector.ToArray()).ToList();
    }
}
