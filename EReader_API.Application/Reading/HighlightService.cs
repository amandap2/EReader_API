using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public class HighlightService(IHighlightRepository repository, IBookRepository bookRepository) : IHighlightService
{
    public async Task<IReadOnlyList<HighlightDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);
        var highlights = await repository.ListAsync(requesterId, bookId, ct);
        return highlights.Select(ToDto).ToList();
    }

    public async Task<HighlightDto> CreateAsync(
        Guid bookId, CreateHighlightRequest req, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);

        if (req.PageNumber < 1)
            throw new ReadingValidationException(["pageNumber deve ser >= 1."]);

        var highlight = new Highlight
        {
            Id = Guid.NewGuid(),
            UserId = requesterId,
            BookId = bookId,
            PageNumber = req.PageNumber,
            TextContent = req.TextContent,
            Color = req.Color,
            Anchor = req.Anchor,
            CreatedAt = DateTime.UtcNow,
        };

        await repository.AddAsync(highlight, ct);
        return ToDto(highlight);
    }

    public async Task<HighlightDto> UpdateAsync(
        Guid id, UpdateHighlightRequest req, Guid requesterId, CancellationToken ct)
    {
        var highlight = await repository.GetByIdAsync(id, ct);
        if (highlight is null || highlight.UserId != requesterId)
            throw new ReadingNotFoundException();

        if (req.Color is not null)
            highlight.Color = req.Color;
        if (req.TextContent is not null)
            highlight.TextContent = req.TextContent;

        await repository.UpdateAsync(highlight, ct);
        return ToDto(highlight);
    }

    public async Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var highlight = await repository.GetByIdAsync(id, ct);
        if (highlight is null || highlight.UserId != requesterId)
            throw new ReadingNotFoundException();

        await repository.RemoveAsync(highlight, ct);
    }

    private static HighlightDto ToDto(Highlight h) =>
        new(h.Id, h.BookId, h.PageNumber, h.TextContent, h.Color, h.Anchor, h.CreatedAt);
}
