using Docnet.Core;
using Docnet.Core.Models;
using EReader_API.Application.Catalog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EReader_API.Infra.Pdf;

public class DocnetPdfInspector : IPdfInspector
{
    public async Task<PdfInspectionResult> InspectAsync(Stream pdfContent, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await pdfContent.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(1080, 1920));
        var pageCount = docReader.GetPageCount();

        byte[]? coverBytes = null;
        if (pageCount > 0)
        {
            using var pageReader = docReader.GetPageReader(0);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
            using var outStream = new MemoryStream();
            image.SaveAsJpeg(outStream);
            coverBytes = outStream.ToArray();
        }

        return new PdfInspectionResult(pageCount, coverBytes);
    }
}
