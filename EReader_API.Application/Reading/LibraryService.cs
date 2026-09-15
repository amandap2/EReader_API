using EReader_API.Application.Catalog;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public class LibraryService(IBookRepository bookRepository, IReadingProgressRepository progressRepository)
    : ILibraryService
{
    public async Task<IReadOnlyList<LibraryItemDto>> GetAsync(Guid requesterId, CancellationToken ct)
    {
        var progressList = await progressRepository.ListByUserAsync(requesterId, ct);
        var progressByBook = progressList.ToDictionary(p => p.BookId);

        var owned = await bookRepository.GetOwnedByUserAsync(requesterId, ct);
        var ownedIds = owned.Select(b => b.Id).ToHashSet();

        var missingIds = progressByBook.Keys.Where(id => !ownedIds.Contains(id)).ToList();
        var fetched = missingIds.Count > 0
            ? await bookRepository.GetByIdsAsync(missingIds, ct)
            : [];

        var items = owned.Concat(fetched)
            .Select(book => new LibraryItemDto(
                BookMapper.ToDto(book),
                progressByBook.TryGetValue(book.Id, out var p)
                    ? new ProgressDto(p.BookId, p.CurrentPage, p.TotalPages, p.PercentComplete, p.LastReadAt)
                    : null))
            .OrderByDescending(i => i.Progress?.LastReadAt ?? DateTime.MinValue)
            .ToList();

        return items;
    }
}
