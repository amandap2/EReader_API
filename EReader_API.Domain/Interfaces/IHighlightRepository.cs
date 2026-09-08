using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces
{
    public interface IHighlightRepository
    {
        Task<IEnumerable<Highlight>> GetHighlightsAsync();
        Task<Highlight> GetByIdAsync(int? id);
        Task<Highlight> CreateAsync(Highlight highlight);
        Task<Highlight> UpdateAsync(Highlight highlight);
        Task<Highlight> RemoveAsync(Highlight highlight);
    }
}
