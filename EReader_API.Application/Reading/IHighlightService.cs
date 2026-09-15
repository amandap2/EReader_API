namespace EReader_API.Application.Reading;

public interface IHighlightService
{
    Task<IReadOnlyList<HighlightDto>> ListAsync(Guid bookId, Guid requesterId, CancellationToken ct);
    Task<HighlightDto> CreateAsync(Guid bookId, CreateHighlightRequest req, Guid requesterId, CancellationToken ct);
    Task<HighlightDto> UpdateAsync(Guid id, UpdateHighlightRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
}
