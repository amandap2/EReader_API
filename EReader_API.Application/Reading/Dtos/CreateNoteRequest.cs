using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record CreateNoteRequest(
    [property: Required] string Content,
    [property: Range(1, int.MaxValue)] int? PageNumber,
    Guid? HighlightId);
