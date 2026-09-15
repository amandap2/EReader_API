using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record UpdateHighlightRequest(
    [property: RegularExpression("^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$")] string? Color,
    string? TextContent);
