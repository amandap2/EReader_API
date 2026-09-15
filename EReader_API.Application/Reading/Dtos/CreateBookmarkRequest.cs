namespace EReader_API.Application.Reading;

public record CreateBookmarkRequest(int PageNumber, string? Label);
