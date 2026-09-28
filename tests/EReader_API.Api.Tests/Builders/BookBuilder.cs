using System.Net.Http.Headers;
using System.Reflection;

namespace EReader_API.Api.Tests.Builders;

public class BookBuilder
{
    private string _title = "Livro de Teste";
    private string? _author = "Autor de Teste";

    public BookBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public BookBuilder WithAuthor(string? author)
    {
        _author = author;
        return this;
    }

    public MultipartFormDataContent Build()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var pdfStream = assembly.GetManifestResourceStream(
            "EReader_API.Api.Tests.Fixtures.minimal.pdf")!;
        var pdfBytes = new byte[pdfStream.Length];
        pdfStream.ReadExactly(pdfBytes);

        var content = new MultipartFormDataContent
        {
            { new StringContent(_title), "Title" },
        };
        if (_author is not null)
            content.Add(new StringContent(_author), "Author");

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "test.pdf");

        return content;
    }
}
