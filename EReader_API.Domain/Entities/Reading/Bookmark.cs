namespace EReader_API.Domain.Entities.Reading;

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}
