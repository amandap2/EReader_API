using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public class ReadingProgressService(IReadingProgressRepository repository, IBookRepository bookRepository)
    : IReadingProgressService
{
    public async Task<ProgressDto?> GetAsync(Guid bookId, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);
        var progress = await repository.GetAsync(requesterId, bookId, ct);
        return progress is null ? null : ToDto(progress);
    }

    public async Task<ProgressDto> UpsertAsync(
        Guid bookId, UpdateProgressRequest req, Guid requesterId, CancellationToken ct)
    {
        var book = await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);

        if (req.CurrentPage < 1 || (book.PageCount is int total && req.CurrentPage > total))
            throw new ReadingValidationException(["currentPage fora do intervalo permitido."]);

        var progress = await repository.GetAsync(requesterId, bookId, ct)
                        ?? new ReadingProgress { Id = Guid.NewGuid(), UserId = requesterId, BookId = bookId };

        progress.CurrentPage = req.CurrentPage;
        progress.TotalPages = book.PageCount;
        progress.LastReadAt = DateTime.UtcNow;
        progress.PercentComplete = ReadingProgressCalculator.PercentComplete(req.CurrentPage, book.PageCount);

        await repository.UpsertAsync(progress, ct);
        return ToDto(progress);
    }

    private static ProgressDto ToDto(ReadingProgress p) =>
        new(p.BookId, p.CurrentPage, p.TotalPages, p.PercentComplete, p.LastReadAt);
}
