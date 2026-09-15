using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

internal static class BookAccessGuard
{
    public static async Task<Book> EnsureAccessibleAsync(
        IBookRepository bookRepository, Guid bookId, Guid requesterId, CancellationToken ct)
    {
        var book = await bookRepository.GetByIdAsync(bookId, ct);
        if (book is null || (book.Source == BookSource.UserUpload && book.OwnerId != requesterId))
            throw new ReadingNotFoundException();

        return book;
    }
}
