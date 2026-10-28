using Microsoft.AspNetCore.Mvc;
using RagApi.Models;
using RagApi.Services;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestController : ControllerBase
{
    private readonly IIngestionService      _ingestion;
    private readonly ILogger<IngestController> _logger;

    // Configurable max upload size (default 50 MB)
    private const long MaxUploadBytes = 50 * 1024 * 1024;

    public IngestController(IIngestionService ingestion, ILogger<IngestController> logger)
    {
        _ingestion = ingestion;
        _logger    = logger;
    }

    /// <summary>
    /// Upload a PDF or text file and ingest it into the knowledge base.
    /// Returns the number of chunks created and the time taken.
    ///
    /// LEARNING NOTE — Why multipart/form-data instead of a JSON body with a file path?
    /// The old design accepted a raw file-system path from the caller, which is a
    /// path-traversal vulnerability: a malicious caller could pass
    /// "C:\Windows\System32\drivers\etc\hosts" and read any file on the server.
    /// Accepting the file bytes directly (IFormFile) eliminates that risk entirely —
    /// the server never touches the caller's file system.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<IngestResponse>> Ingest(
        [FromForm] IFormFile file,
        [FromForm] string    userId = "public",
        CancellationToken    ct     = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required.");

        if (file.Length > MaxUploadBytes)
            return BadRequest($"File exceeds the {MaxUploadBytes / 1024 / 1024} MB limit.");

        // Accept PDF and plain-text only
        string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".txt"))
            return BadRequest("Only .pdf and .txt files are supported.");

        _logger.LogInformation("Ingesting {FileName} ({Bytes} bytes) for user {UserId}",
            file.FileName, file.Length, userId);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var stream = file.OpenReadStream();
            int chunks = await _ingestion.IngestAsync(file.FileName, stream, userId, ct);
            sw.Stop();

            return Ok(new IngestResponse
            {
                FileName  = file.FileName,
                Chunks    = chunks,
                TimeTaken = $"{sw.Elapsed.TotalSeconds:F1}s",
                UserId    = userId
            });
        }
        catch (InvalidOperationException ex)
        {
            // Scanned PDF, empty doc, etc. — safe to surface
            return UnprocessableEntity(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {FileName}", file.FileName);
            // Never return ex.Message directly — it may contain internal path info
            return StatusCode(500, "Ingestion failed. Check server logs for details.");
        }
    }
}
