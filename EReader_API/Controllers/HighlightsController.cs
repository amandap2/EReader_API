using EReader_API.Application.Common;
using EReader_API.Application.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Authorize]
public class HighlightsController(IHighlightService service) : ControllerBase
{
    [HttpGet("api/books/{bookId:guid}/highlights")]
    public async Task<IActionResult> List(Guid bookId, CancellationToken ct) =>
        Ok(await service.ListAsync(bookId, User.GetUserId(), ct));

    [HttpPost("api/books/{bookId:guid}/highlights")]
    public async Task<IActionResult> Create(Guid bookId, CreateHighlightRequest req, CancellationToken ct)
    {
        var dto = await service.CreateAsync(bookId, req, User.GetUserId(), ct);
        return CreatedAtAction(nameof(List), new { bookId }, dto);
    }

    [HttpPatch("api/highlights/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateHighlightRequest req, CancellationToken ct) =>
        Ok(await service.UpdateAsync(id, req, User.GetUserId(), ct));

    [HttpDelete("api/highlights/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, User.GetUserId(), ct);
        return NoContent();
    }
}
