using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record UpdateNoteRequest([property: Required] string Content);
