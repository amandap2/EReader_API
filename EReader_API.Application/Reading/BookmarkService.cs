using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public class BookmarkService(IBookmarkRepository repository, IBookRepository bookRepository) : IBookmarkService
{
    public async Task<IReadOnlyList<BookmarkDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);
        var bookmarks = await repository.ListAsync(requesterId, bookId, ct);
        return bookmarks.Select(ToDto).ToList();
    }

    public async Task<BookmarkDto> CreateAsync(
        Guid bookId, CreateBookmarkRequest req, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);

        if (req.PageNumber < 1)
            throw new ReadingValidationException(["pageNumber deve ser >= 1."]);

        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            UserId = requesterId,
            BookId = bookId,
            PageNumber = req.PageNumber,
            Label = req.Label,
            CreatedAt = DateTime.UtcNow,
        };

        await repository.AddAsync(bookmark, ct);
        return ToDto(bookmark);
    }

    public async Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var bookmark = await repository.GetByIdAsync(id, ct);
        if (bookmark is null || bookmark.UserId != requesterId)
            throw new ReadingNotFoundException();

        await repository.RemoveAsync(bookmark, ct);
    }

    private static BookmarkDto ToDto(Bookmark b) => new(b.Id, b.BookId, b.PageNumber, b.Label, b.CreatedAt);
}
