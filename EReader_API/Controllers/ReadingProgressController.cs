using EReader_API.Application.Common;
using EReader_API.Application.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/books/{bookId:guid}/progress")]
[Authorize]
public class ReadingProgressController(IReadingProgressService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid bookId, CancellationToken ct)
    {
        var dto = await service.GetAsync(bookId, User.GetUserId(), ct);
        return dto is null ? NoContent() : Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(Guid bookId, UpdateProgressRequest req, CancellationToken ct) =>
        Ok(await service.UpsertAsync(bookId, req, User.GetUserId(), ct));
}
