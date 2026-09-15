namespace EReader_API.Application.Reading;

public record ProgressDto(
    Guid BookId, int CurrentPage, int? TotalPages, decimal PercentComplete, DateTime LastReadAt);
