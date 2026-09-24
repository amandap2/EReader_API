using EReader_API.Application.Common;
using EReader_API.Application.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Authorize]
public class BookmarksController(IBookmarkService service) : ControllerBase
{
    [HttpGet("api/books/{bookId:guid}/bookmarks")]
    public async Task<IActionResult> List(Guid bookId, CancellationToken ct) =>
        Ok(await service.ListAsync(bookId, User.GetUserId(), ct));

    [HttpPost("api/books/{bookId:guid}/bookmarks")]
    public async Task<IActionResult> Create(Guid bookId, CreateBookmarkRequest req, CancellationToken ct)
    {
        var dto = await service.CreateAsync(bookId, req, User.GetUserId(), ct);
        return CreatedAtAction(nameof(List), new { bookId }, dto);
    }

    [HttpDelete("api/bookmarks/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}
