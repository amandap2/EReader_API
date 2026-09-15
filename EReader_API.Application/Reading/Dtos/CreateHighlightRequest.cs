using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record CreateHighlightRequest(
    [property: Range(1, int.MaxValue)] int PageNumber,
    [property: Required] string TextContent,
    [property: RegularExpression("^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$")] string? Color,
    string? Anchor);
