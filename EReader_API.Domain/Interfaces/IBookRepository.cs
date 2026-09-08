using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Domain.Interfaces
{
    public interface IBookRepository
    {
        Task<IEnumerable<Book>> GetBooksAsync();
        Task<Book> GetByIdAsync(int? id);
        Task<Book> CreateAsync(Book book);
        Task<Book> UpdateAsync(Book book);
        Task<Book> RemoveAsync(Book book);
    }
}
