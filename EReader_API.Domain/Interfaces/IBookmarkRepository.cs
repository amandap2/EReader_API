using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IBookmarkRepository
{
    Task<IReadOnlyList<Bookmark>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Bookmark?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Bookmark bookmark, CancellationToken ct);
    Task RemoveAsync(Bookmark bookmark, CancellationToken ct);
}
