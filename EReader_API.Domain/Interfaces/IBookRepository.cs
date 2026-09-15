using EReader_API.Domain.Common;
using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Domain.Interfaces;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Book>> GetOwnedByUserAsync(Guid ownerId, CancellationToken ct);
    Task<PagedResult<Book>> QueryAsync(BookQuery query, CancellationToken ct);
    Task AddAsync(Book book, CancellationToken ct);
    Task UpdateAsync(Book book, CancellationToken ct);
    Task RemoveAsync(Book book, CancellationToken ct);
}
