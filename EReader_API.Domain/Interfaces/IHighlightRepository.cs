using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IHighlightRepository
{
    Task<IReadOnlyList<Highlight>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Highlight?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Highlight highlight, CancellationToken ct);
    Task UpdateAsync(Highlight highlight, CancellationToken ct);
    Task RemoveAsync(Highlight highlight, CancellationToken ct);
}
