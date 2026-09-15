using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public class NoteService(INoteRepository repository, IHighlightRepository highlightRepository, IBookRepository bookRepository)
    : INoteService
{
    public async Task<IReadOnlyList<NoteDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);
        var notes = await repository.ListAsync(requesterId, bookId, ct);
        return notes.Select(ToDto).ToList();
    }

    public async Task<NoteDto> CreateAsync(
        Guid bookId, CreateNoteRequest req, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);

        if (req.HighlightId is Guid highlightId)
        {
            var highlight = await highlightRepository.GetByIdAsync(highlightId, ct);
            if (highlight is null || highlight.UserId != requesterId || highlight.BookId != bookId)
                throw new ReadingValidationException(["highlightId inválido para este livro/usuário."]);
        }

        var now = DateTime.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = requesterId,
            BookId = bookId,
            PageNumber = req.PageNumber,
            HighlightId = req.HighlightId,
            Content = req.Content,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await repository.AddAsync(note, ct);
        return ToDto(note);
    }

    public async Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest req, Guid requesterId, CancellationToken ct)
    {
        var note = await repository.GetByIdAsync(id, ct);
        if (note is null || note.UserId != requesterId)
            throw new ReadingNotFoundException();

        note.Content = req.Content;
        note.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateAsync(note, ct);
        return ToDto(note);
    }

    public async Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var note = await repository.GetByIdAsync(id, ct);
        if (note is null || note.UserId != requesterId)
            throw new ReadingNotFoundException();

        await repository.RemoveAsync(note, ct);
    }

    private static NoteDto ToDto(Note n) =>
        new(n.Id, n.BookId, n.PageNumber, n.HighlightId, n.Content, n.CreatedAt, n.UpdatedAt);
}
