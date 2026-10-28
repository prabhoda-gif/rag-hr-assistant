namespace RagApi.Services.Pdf;

/// <summary>
/// A parsed page — text content plus the 1-based page number from the source document.
/// Keeping text-per-page means every chunk can carry a real citation page number.
/// </summary>
public record ParsedPage(int PageNumber, string Text);

public interface IPdfParser
{
    /// <summary>
    /// Extracts text from a PDF stream, one entry per page.
    /// Never returns null; empty pages produce an entry with empty Text.
    /// </summary>
    IReadOnlyList<ParsedPage> Parse(Stream pdfStream);
}
