using System.ComponentModel.DataAnnotations;
using EReader_API.Application.Catalog;
using EReader_API.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/books")]
[Authorize]
public class BookController(IBookService bookService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? scope, [FromQuery] string? search, [FromQuery] string? author,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] string? sort = null,
        CancellationToken ct = default) =>
        Ok(await bookService.ListAsync(scope, search, author, page, pageSize, sort, User.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await bookService.GetAsync(id, User.GetUserId(), ct));

    [HttpPost]
    [RequestSizeLimit(100_000_000)]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Upload([FromForm] UploadBookForm form, CancellationToken ct)
    {
        var req = new UploadBookRequest(
            form.Title, form.Author, form.Description, form.Language, form.PublishedDate, form.PageCount,
            form.File.OpenReadStream(), form.File.ContentType, form.File.FileName, form.File.Length);
        var dto = await bookService.UploadAsync(req, User.GetUserId(), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBookRequest req, CancellationToken ct) =>
        Ok(await bookService.UpdateAsync(id, req, User.GetUserId(), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await bookService.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        var file = await bookService.OpenFileAsync(id, User.GetUserId(), ct);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> GetCover(Guid id, CancellationToken ct)
    {
        var cover = await bookService.OpenCoverAsync(id, User.GetUserId(), ct);
        return cover is null
            ? NotFound()
            : File(cover.Content, cover.ContentType, cover.FileName, enableRangeProcessing: true);
    }
}

public class UploadBookForm
{
    [Required, StringLength(300)]
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    [RegularExpression("^[a-z]{2}(-[A-Z]{2})?$")]
    public string? Language { get; set; }
    public DateTime? PublishedDate { get; set; }
    [Range(1, int.MaxValue)]
    public int? PageCount { get; set; }
    [Required]
    public IFormFile File { get; set; } = null!;
}
