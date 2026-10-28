namespace RagApi.Models;

// NOTE: IngestRequest is no longer used by IngestController.
// The controller now accepts multipart/form-data (IFormFile) directly.
// Kept here in case you add a background worker that re-processes stored files.

public class IngestResponse
{
    public string FileName  { get; set; } = string.Empty;
    public int    Chunks    { get; set; }
    public string TimeTaken { get; set; } = string.Empty;
    public string UserId    { get; set; } = string.Empty;
}
