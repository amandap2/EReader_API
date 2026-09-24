using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record UpdateNoteRequest([Required] string Content);
