namespace EReader_API.Application.Reading;

public record CreateHighlightRequest(int PageNumber, string TextContent, string? Color, string? Anchor);
