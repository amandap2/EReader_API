namespace EReader_API.Application.Reading;

public interface IBookmarkService
{
    Task<IReadOnlyList<BookmarkDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct);
    Task<BookmarkDto> CreateAsync(Guid bookId, CreateBookmarkRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
}
