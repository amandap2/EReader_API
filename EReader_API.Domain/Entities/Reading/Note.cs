namespace EReader_API.Domain.Entities.Reading;

public class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int? PageNumber { get; set; }
    public Guid? HighlightId { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
