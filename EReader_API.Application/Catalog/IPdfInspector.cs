namespace EReader_API.Application.Catalog;

public record PdfInspectionResult(int PageCount, byte[]? CoverJpegBytes);

public interface IPdfInspector
{
    Task<PdfInspectionResult> InspectAsync(Stream pdfContent, CancellationToken ct);
}
