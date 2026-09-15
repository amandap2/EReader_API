namespace EReader_API.Application.Catalog;

public record UpdateBookRequest(
    string Title, string? Author, string? Description, string? Language,
    DateTime? PublishedDate, int? PageCount);
