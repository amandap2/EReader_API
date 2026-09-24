namespace EReader_API.Application.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string contentType, string suggestedName, CancellationToken ct);
    Task<string> SaveCoverAsync(Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string fileKey, CancellationToken ct);
    Task DeleteAsync(string fileKey, CancellationToken ct);
    Task<bool> ExistsAsync(string fileKey, CancellationToken ct);
}
