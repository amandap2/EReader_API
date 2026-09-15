namespace EReader_API.Domain.Entities.Catalog;

public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string Format { get; set; } = "pdf";
    public BookSource Source { get; set; }
    public Guid? OwnerId { get; set; }
    public string FileKey { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }
    public string? CoverImageKey { get; set; }
    public DateTime? PublishedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
