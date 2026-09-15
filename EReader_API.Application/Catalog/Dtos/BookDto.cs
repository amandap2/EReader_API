namespace EReader_API.Application.Catalog;

public record BookDto(
    Guid Id, string Title, string? Author, string? Description, string? Language,
    string Format, string Source, Guid? OwnerId, long FileSizeBytes, int? PageCount,
    bool HasCoverImage, DateTime? PublishedDate, DateTime CreatedAt, DateTime UpdatedAt);
