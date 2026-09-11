using System.Security.Claims;

namespace EReader_API.Application.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value
                  ?? throw new InvalidOperationException("Claim 'sub' ausente.");
        return Guid.Parse(sub);
    }
}
