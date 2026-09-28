using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EReader_API.Api.Tests.Builders;
using EReader_API.Api.Tests.Fixtures;
using EReader_API.Api.Tests.Helpers;
using EReader_API.Application.Catalog;
using FluentAssertions;

namespace EReader_API.Api.Tests;

public class BooksEndpointsTests(EReaderApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task UploadDownloadDelete_FullFlow_Succeeds()
    {
        await AuthHelper.RegisterAndLoginAsync(Client);

        // upload
        var upload = await Client.PostAsync("/api/books", new BookBuilder().WithTitle("Livro E2E").Build());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var book = await upload.Content.ReadFromJsonAsync<BookDto>();
        book!.Title.Should().Be("Livro E2E");

        // download com Range
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{book.Id}/file");
        request.Headers.Range = new RangeHeaderValue(0, 99);
        var download = await Client.SendAsync(request);
        download.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        download.Content.Headers.ContentRange.Should().NotBeNull();

        // apagar
        var delete = await Client.DeleteAsync($"/api/books/{book.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await Client.GetAsync($"/api/books/{book.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_ThenGet_ReturnsUploadedBook()
    {
        await AuthHelper.RegisterAndLoginAsync(Client);

        var upload = await Client.PostAsync("/api/books",
            new BookBuilder().WithTitle("Outro Livro").WithAuthor("Fulano").Build());
        var created = await upload.Content.ReadFromJsonAsync<BookDto>();

        var get = await Client.GetAsync($"/api/books/{created!.Id}");

        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await get.Content.ReadFromJsonAsync<BookDto>();
        fetched!.Title.Should().Be("Outro Livro");
        fetched.Author.Should().Be("Fulano");
    }

    [Fact]
    public async Task Get_AnotherUsersBook_Returns404()
    {
        await AuthHelper.RegisterAndLoginAsync(Client);
        var upload = await Client.PostAsync("/api/books", new BookBuilder().Build());
        var book = await upload.Content.ReadFromJsonAsync<BookDto>();

        using var otherClient = Factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(otherClient);

        var get = await otherClient.GetAsync($"/api/books/{book!.Id}");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
