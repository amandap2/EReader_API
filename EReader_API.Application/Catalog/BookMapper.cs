using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Application.Catalog;

public static class BookMapper
{
    public static BookDto ToDto(Book book) => new(
        book.Id, book.Title, book.Author, book.Description, book.Language,
        book.Format, book.Source.ToString(), book.OwnerId, book.FileSizeBytes, book.PageCount,
        book.CoverImageKey is not null, book.PublishedDate, book.CreatedAt, book.UpdatedAt);
}
