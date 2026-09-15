using EReader_API.Application.Common;
using EReader_API.Application.Reading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/library")]
[Authorize]
public class LibraryController(ILibraryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await service.GetAsync(User.GetUserId(), ct));
}
