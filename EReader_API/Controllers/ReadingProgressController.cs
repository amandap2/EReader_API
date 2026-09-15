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
        try
        {
            var dto = await service.GetAsync(bookId, User.GetUserId(), ct);
            return dto is null ? NoContent() : Ok(dto);
        }
        catch (ReadingNotFoundException) { return NotFound(); }
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(Guid bookId, UpdateProgressRequest req, CancellationToken ct)
    {
        try { return Ok(await service.UpsertAsync(bookId, req, User.GetUserId(), ct)); }
        catch (ReadingNotFoundException) { return NotFound(); }
        catch (ReadingValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }
}
