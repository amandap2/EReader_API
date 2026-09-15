using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface INoteRepository
{
    Task<IReadOnlyList<Note>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Note?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Note note, CancellationToken ct);
    Task UpdateAsync(Note note, CancellationToken ct);
    Task RemoveAsync(Note note, CancellationToken ct);
}
