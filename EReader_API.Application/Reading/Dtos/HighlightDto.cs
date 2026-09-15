namespace EReader_API.Application.Reading;

public record HighlightDto(
    Guid Id, Guid BookId, int PageNumber, string TextContent, string? Color, string? Anchor,
    DateTime CreatedAt);
