using EReader_API.Application.Common;
using EReader_API.Application.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Authorize]
public class NotesController(INoteService service) : ControllerBase
{
    [HttpGet("api/books/{bookId:guid}/notes")]
    public async Task<IActionResult> List(Guid bookId, CancellationToken ct)
    {
        try { return Ok(await service.ListAsync(bookId, User.GetUserId(), ct)); }
        catch (ReadingNotFoundException) { return NotFound(); }
    }

    [HttpPost("api/books/{bookId:guid}/notes")]
    public async Task<IActionResult> Create(Guid bookId, CreateNoteRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await service.CreateAsync(bookId, req, User.GetUserId(), ct);
            return CreatedAtAction(nameof(List), new { bookId }, dto);
        }
        catch (ReadingNotFoundException) { return NotFound(); }
        catch (ReadingValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }

    [HttpPatch("api/notes/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateNoteRequest req, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, req, User.GetUserId(), ct)); }
        catch (ReadingNotFoundException) { return NotFound(); }
    }

    [HttpDelete("api/notes/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, User.GetUserId(), ct); return NoContent(); }
        catch (ReadingNotFoundException) { return NotFound(); }
    }
}
