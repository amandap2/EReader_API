using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record CreateBookmarkRequest([property: Range(1, int.MaxValue)] int PageNumber, string? Label);
