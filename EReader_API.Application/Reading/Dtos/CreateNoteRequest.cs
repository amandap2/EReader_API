using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record CreateNoteRequest(
    [Required] string Content,
    [Range(1, int.MaxValue)] int? PageNumber,
    Guid? HighlightId);
