using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Catalog;

public record UpdateBookRequest(
    [property: Required, StringLength(300)] string Title,
    string? Author,
    string? Description,
    [property: RegularExpression("^[a-z]{2}(-[A-Z]{2})?$")] string? Language,
    DateTime? PublishedDate,
    [property: Range(1, int.MaxValue)] int? PageCount);
