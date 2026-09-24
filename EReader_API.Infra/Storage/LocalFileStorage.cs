using EReader_API.Application.Storage;
using Microsoft.Extensions.Hosting;

namespace EReader_API.Infra.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(FileStorageOptions options, IHostEnvironment env)
    {
        _rootPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.RootPath));
    }

    public async Task<string> SaveAsync(Stream content, string contentType, string suggestedName, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var fileKey = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.pdf";
        var fullPath = ResolvePath(fileKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fileStream, ct);

        return fileKey;
    }

    public async Task<string> SaveCoverAsync(Stream content, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var fileKey = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}-cover.jpg";
        var fullPath = ResolvePath(fileKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fileStream, ct);

        return fileKey;
    }

    public Task<Stream> OpenReadAsync(string fileKey, CancellationToken ct)
    {
        var fullPath = ResolvePath(fileKey);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string fileKey, CancellationToken ct)
    {
        var fullPath = ResolvePath(fileKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string fileKey, CancellationToken ct)
    {
        var fullPath = ResolvePath(fileKey);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string ResolvePath(string fileKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, fileKey));
        var relative = Path.GetRelativePath(_rootPath, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"fileKey '{fileKey}' resolve para fora da raiz de armazenamento.");

        return fullPath;
    }
}
