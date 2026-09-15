namespace EReader_API.Application.Reading;

public interface ILibraryService
{
    Task<IReadOnlyList<LibraryItemDto>> GetAsync(Guid requesterId, CancellationToken ct);
}
