namespace RagApi.Models;

public class AskRequest
{
    public string Question       { get; set; } = string.Empty;
    public string UserId         { get; set; } = "public";      // which user is asking
    public string ConversationId { get; set; } = "default";     // which conversation thread
}

public class ChunkResult
{
    public string FileName   { get; set; } = string.Empty;
    public int    ChunkIndex { get; set; }
    public int    PageNumber { get; set; }   // real PDF page, 0 = unknown
    public string ChunkText  { get; set; } = string.Empty;
    public double Distance   { get; set; }
}

public class AskResponse
{
    public string            Answer         { get; set; } = string.Empty;
    public List<ChunkResult> Sources        { get; set; } = [];
    public string            ConversationId { get; set; } = string.Empty;
}
