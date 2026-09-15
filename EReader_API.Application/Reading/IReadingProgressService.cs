namespace EReader_API.Application.Reading;

public interface IReadingProgressService
{
    Task<ProgressDto?> GetAsync(Guid bookId, Guid requesterId, CancellationToken ct);
    Task<ProgressDto> UpsertAsync(Guid bookId, UpdateProgressRequest req, Guid requesterId, CancellationToken ct);
}
