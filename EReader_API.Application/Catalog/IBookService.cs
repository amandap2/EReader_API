using EReader_API.Domain.Common;

namespace EReader_API.Application.Catalog;

public interface IBookService
{
    Task<PagedResult<BookDto>> ListAsync(string? scope, string? search, string? author,
        int page, int pageSize, string? sort, Guid requesterId, CancellationToken ct);
    Task<BookDto> GetAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<BookDto> UploadAsync(UploadBookRequest req, Guid ownerId, CancellationToken ct);
    Task<BookDto> UpdateAsync(Guid id, UpdateBookRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<FileDownload> OpenFileAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<FileDownload?> OpenCoverAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task DeleteAllOwnedByUserAsync(Guid ownerId, CancellationToken ct);
}
