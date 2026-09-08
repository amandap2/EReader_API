using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces
{
    public interface IBookmarkRepository
    {
        Task<IEnumerable<Bookmark>> GetBookmarksAsync();
        Task<Bookmark> GetByIdAsync(int? id);
        Task<Bookmark> CreateAsync(Bookmark bookmark);
        Task<Bookmark> UpdateAsync(Bookmark bookmark);
        Task<Bookmark> RemoveAsync(Bookmark bookmark);
    }
}
