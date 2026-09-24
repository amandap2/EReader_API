using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Catalog;

public record UpdateBookRequest(
    [Required, StringLength(300)] string Title,
    string? Author,
    string? Description,
    [RegularExpression("^[a-z]{2}(-[A-Z]{2})?$")] string? Language,
    DateTime? PublishedDate,
    [Range(1, int.MaxValue)] int? PageCount);
