namespace EReader_API.Domain.Entities.Reading;

public class Highlight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string TextContent { get; set; } = "";
    public string? Color { get; set; }
    public string? Anchor { get; set; }
    public DateTime CreatedAt { get; set; }
}
