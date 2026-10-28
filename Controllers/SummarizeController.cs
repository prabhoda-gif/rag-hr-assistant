using Microsoft.AspNetCore.Mvc;
using RagApi.Services;

namespace RagApi.Controllers;

// =============================================================================
// SummarizeController — POST /api/summarize
//
// LEARNING NOTE — Why a separate controller?
// Summarization is a distinct capability from Q&A. Keeping it in its own
// controller follows the Single Responsibility Principle — each controller
// has one job. It also makes the API surface cleaner and easier to document.
// =============================================================================

[ApiController]
[Route("api/[controller]")]
public class SummarizeController : ControllerBase
{
    private readonly ISummarizationService  _summarizer;
    private readonly ILogger<SummarizeController> _logger;

    public SummarizeController(
        ISummarizationService summarizer,
        ILogger<SummarizeController> logger)
    {
        _summarizer = summarizer;
        _logger     = logger;
    }

    // -------------------------------------------------------------------------
    // Streaming summary — returns text/event-stream (same SSE pattern as /ask/stream)
    //
    // LEARNING NOTE — Why stream the summary?
    // A summary of a large document can take 5-10 seconds to generate.
    // Streaming shows the user tokens as they arrive rather than a blank
    // screen followed by a wall of text. Same UX pattern as ChatGPT.
    // -------------------------------------------------------------------------
    [HttpPost("stream")]
    public async Task SummarizeStreaming([FromBody] SummarizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            Response.StatusCode = 400;
            return;
        }

        _logger.LogInformation("[{User}] Summarizing {File}", request.UserId, request.FileName);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var token in _summarizer.SummarizeStreamingAsync(
            request.FileName, request.UserId, ct))
        {
            await Response.WriteAsync($"data: {token}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
    }
}

// -------------------------------------------------------------------------
// Request model — kept simple: just the file name and user id
// -------------------------------------------------------------------------
public class SummarizeRequest
{
    public string FileName { get; set; } = string.Empty;
    public string UserId   { get; set; } = "public";
}
