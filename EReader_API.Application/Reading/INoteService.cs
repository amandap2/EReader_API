namespace EReader_API.Application.Reading;

public interface INoteService
{
    Task<IReadOnlyList<NoteDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct);
    Task<NoteDto> CreateAsync(Guid bookId, CreateNoteRequest req, Guid requesterId, CancellationToken ct);
    Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
}
