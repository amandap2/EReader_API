namespace EReader_API.Application.Reading;

public record NoteDto(
    Guid Id, Guid BookId, int? PageNumber, Guid? HighlightId, string Content,
    DateTime CreatedAt, DateTime UpdatedAt);
