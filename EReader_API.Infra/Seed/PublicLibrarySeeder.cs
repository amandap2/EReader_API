using System.Text.Json;
using EReader_API.Application.Storage;
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EReader_API.Infra.Seed;

public class PublicLibrarySeeder(
    ApplicationDbContext db,
    IFileStorage fileStorage,
    IHostEnvironment env,
    ILogger<PublicLibrarySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var manifestPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "docs", "seed", "public-domain.json"));
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("Manifesto de seed não encontrado em {Path}; pulando seed da biblioteca pública.", manifestPath);
            return;
        }

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<List<SeedEntry>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var filesDir = Path.Combine(Path.GetDirectoryName(manifestPath)!, "files");
        var addedAny = false;

        foreach (var entry in manifest)
        {
            var alreadySeeded = await db.Books.AnyAsync(
                b => b.Title == entry.Title && b.Author == entry.Author, ct);
            if (alreadySeeded)
                continue;

            var filePath = Path.Combine(filesDir, entry.SourceFile);
            if (!File.Exists(filePath))
            {
                logger.LogWarning(
                    "Arquivo de seed '{SourceFile}' não encontrado em {Dir}; pulando '{Title}'.",
                    entry.SourceFile, filesDir, entry.Title);
                continue;
            }

            await using var stream = File.OpenRead(filePath);
            var fileKey = await fileStorage.SaveAsync(stream, "application/pdf", entry.SourceFile, ct);

            var now = DateTime.UtcNow;
            db.Books.Add(new Book
            {
                Id = Guid.NewGuid(),
                Title = entry.Title,
                Author = entry.Author,
                Language = entry.Language,
                Format = "pdf",
                Source = BookSource.PublicDomain,
                OwnerId = null,
                FileKey = fileKey,
                FileSizeBytes = new FileInfo(filePath).Length,
                PublishedDate = entry.PublishedDate,
                CreatedAt = now,
                UpdatedAt = now,
            });
            addedAny = true;
        }

        if (addedAny)
            await db.SaveChangesAsync(ct);
    }

    private record SeedEntry(string Title, string? Author, string? Language, DateTime? PublishedDate, string SourceFile);
}
