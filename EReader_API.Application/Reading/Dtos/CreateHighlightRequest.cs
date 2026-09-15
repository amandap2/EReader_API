using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record CreateHighlightRequest(
    [Range(1, int.MaxValue)] int PageNumber,
    [Required] string TextContent,
    [RegularExpression("^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$")] string? Color,
    string? Anchor);
