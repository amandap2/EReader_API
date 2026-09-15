namespace EReader_API.Application.Catalog;

public record UploadBookRequest(
    string Title, string? Author, string? Description, string? Language,
    DateTime? PublishedDate, int? PageCount,
    Stream FileContent, string ContentType, string FileName, long FileSizeBytes);
