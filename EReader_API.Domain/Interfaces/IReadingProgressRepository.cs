using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IReadingProgressRepository
{
    Task<ReadingProgress?> GetAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<IReadOnlyList<ReadingProgress>> ListByUserAsync(Guid userId, CancellationToken ct);
    Task UpsertAsync(ReadingProgress progress, CancellationToken ct);
}
