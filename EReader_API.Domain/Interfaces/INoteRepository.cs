using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces
{
    public interface INoteRepository
    {
        Task<IEnumerable<Note>> GetNotesAsync();
        Task<Note> GetByIdAsync(int? id);
        Task<Note> CreateAsync(Note note);
        Task<Note> UpdateAsync(Note note);
        Task<Note> RemoveAsync(Note note);
    }
}
