namespace EReader_API.Application.Reading;

public record CreateNoteRequest(string Content, int? PageNumber, Guid? HighlightId);
