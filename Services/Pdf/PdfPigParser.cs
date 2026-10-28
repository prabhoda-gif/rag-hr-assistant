using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RagApi.Services.Pdf;

/// <summary>
/// Extracts text from a PDF using PdfPig, preserving page numbers.
///
/// LEARNING NOTE — Why PdfPig?
/// PdfPig is a pure .NET library with no native dependencies, so it runs
/// identically on Windows, Linux and Docker. It preserves page boundaries,
/// which is what makes real citations ("page 7 of policy.pdf") possible.
///
/// LIMITATION: PdfPig extracts text from text-layer PDFs only.
/// Scanned/image PDFs will return empty or near-empty pages.
/// OCR (e.g. Tesseract) is a separate optional capability — not wired here.
/// </summary>
public class PdfPigParser : IPdfParser
{
    public IReadOnlyList<ParsedPage> Parse(Stream pdfStream)
    {
        var pages = new List<ParsedPage>();

        using var document = PdfDocument.Open(pdfStream);

        foreach (Page page in document.GetPages())
        {
            // GetWords() uses spatial clustering — better than raw character order
            // for multi-column layouts. Join words back to plain text.
            string text = string.Join(" ", page.GetWords().Select(w => w.Text));
            pages.Add(new ParsedPage(page.Number, text));
        }

        return pages;
    }
}
