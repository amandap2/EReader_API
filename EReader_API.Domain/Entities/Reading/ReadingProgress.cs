namespace EReader_API.Domain.Entities.Reading;

public class ReadingProgress
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int CurrentPage { get; set; }
    public int? TotalPages { get; set; }
    public decimal PercentComplete { get; set; }
    public DateTime LastReadAt { get; set; }
}
