namespace EReader_API.Application.Reading;

public record BookmarkDto(Guid Id, Guid BookId, int PageNumber, string? Label, DateTime CreatedAt);
