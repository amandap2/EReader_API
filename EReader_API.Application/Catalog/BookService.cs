using System.Text;
using EReader_API.Application.Common;
using EReader_API.Application.Common.Exceptions;
using EReader_API.Application.Storage;
using EReader_API.Domain.Common;
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EReader_API.Application.Catalog;

public class BookService(
    IBookRepository repository,
    IFileStorage fileStorage,
    FileStorageOptions options,
    IPdfInspector pdfInspector,
    IReadingProgressRepository progressRepository,
    ILogger<BookService> logger) : IBookService
{
    private static readonly HashSet<string> AllowedSorts = new(StringComparer.Ordinal)
    {
        "title", "-title", "author", "-author", "createdAt", "-createdAt",
    };

    public async Task<PagedResult<BookDto>> ListAsync(string? scope, string? search, string? author,
        int page, int pageSize, string? sort, Guid requesterId, CancellationToken ct)
    {
        if (sort is not null && !AllowedSorts.Contains(sort))
            throw new BookValidationException([$"Valor de 'sort' inválido: '{sort}'."]);

        var bookScope = ParseScope(scope);
        var query = new BookQuery(requesterId, bookScope, search, author, page, pageSize, sort);
        var result = await repository.QueryAsync(query, ct);

        return new PagedResult<BookDto>(
            result.Items.Select(BookMapper.ToDto).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<BookDto> GetAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var book = await GetAuthorizedAsync(id, requesterId, ct);
        return BookMapper.ToDto(book);
    }

    public async Task<BookDto> UploadAsync(UploadBookRequest req, Guid ownerId, CancellationToken ct)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
            errors.Add("Title é obrigatório e deve ter até 300 caracteres.");
        if (req.PageCount is < 1)
            errors.Add("PageCount deve ser >= 1 quando informado.");
        if (!string.Equals(req.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            errors.Add("Content-Type do arquivo deve ser application/pdf.");
        if (req.FileSizeBytes > options.MaxUploadBytes)
            errors.Add($"Arquivo excede o tamanho máximo permitido de {options.MaxUploadBytes} bytes.");

        var magic = new byte[5];
        var bytesRead = await req.FileContent.ReadAsync(magic.AsMemory(0, 5), ct);
        if (bytesRead < 5 || Encoding.ASCII.GetString(magic) != "%PDF-")
            errors.Add("Arquivo não é um PDF válido (magic bytes ausentes).");

        if (errors.Count > 0)
            throw new BookValidationException(errors);

        if (req.FileContent.CanSeek)
            req.FileContent.Seek(0, SeekOrigin.Begin);

        var fileKey = await fileStorage.SaveAsync(req.FileContent, req.ContentType, req.FileName, ct);

        var pageCount = req.PageCount;
        string? coverImageKey = null;
        try
        {
            if (req.FileContent.CanSeek)
                req.FileContent.Seek(0, SeekOrigin.Begin);

            var inspection = await pdfInspector.InspectAsync(req.FileContent, ct);
            pageCount = inspection.PageCount;
            if (inspection.CoverJpegBytes is { } jpeg)
            {
                using var coverStream = new MemoryStream(jpeg);
                coverImageKey = await fileStorage.SaveCoverAsync(coverStream, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao extrair PageCount/capa do PDF recém-enviado.");
        }

        var now = DateTime.UtcNow;
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = req.Title,
            Author = req.Author,
            Description = req.Description,
            Language = req.Language,
            Format = "pdf",
            Source = BookSource.UserUpload,
            OwnerId = ownerId,
            FileKey = fileKey,
            FileSizeBytes = req.FileSizeBytes,
            PageCount = pageCount,
            CoverImageKey = coverImageKey,
            PublishedDate = req.PublishedDate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await repository.AddAsync(book, ct);
        await PropagateProgressAsync(book.Id, book.PageCount, ct);
        return BookMapper.ToDto(book);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpdateBookRequest req, Guid requesterId, CancellationToken ct)
    {
        var book = await GetAuthorizedAsync(id, requesterId, ct);

        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
            throw new BookValidationException(["Title é obrigatório e deve ter até 300 caracteres."]);
        if (req.PageCount is < 1)
            throw new BookValidationException(["PageCount deve ser >= 1 quando informado."]);

        book.Title = req.Title;
        book.Author = req.Author;
        book.Description = req.Description;
        book.Language = req.Language;
        book.PublishedDate = req.PublishedDate;
        book.PageCount = req.PageCount;
        book.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateAsync(book, ct);
        await PropagateProgressAsync(book.Id, book.PageCount, ct);
        return BookMapper.ToDto(book);
    }

    public async Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var book = await GetAuthorizedAsync(id, requesterId, ct);
        if (book.Source == BookSource.PublicDomain)
            throw new BookForbiddenException();

        await fileStorage.DeleteAsync(book.FileKey, ct);
        await repository.RemoveAsync(book, ct);
    }

    public async Task<FileDownload> OpenFileAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var book = await GetAuthorizedAsync(id, requesterId, ct);
        var stream = await fileStorage.OpenReadAsync(book.FileKey, ct);
        return new FileDownload(stream, "application/pdf", $"{book.Title}.pdf");
    }

    public async Task<FileDownload?> OpenCoverAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var book = await GetAuthorizedAsync(id, requesterId, ct);
        if (book.CoverImageKey is null)
            return null;

        var stream = await fileStorage.OpenReadAsync(book.CoverImageKey, ct);
        return new FileDownload(stream, "image/jpeg", $"{book.Title}-cover.jpg");
    }

    public async Task DeleteAllOwnedByUserAsync(Guid ownerId, CancellationToken ct)
    {
        var books = await repository.GetOwnedByUserAsync(ownerId, ct);
        foreach (var book in books)
        {
            await fileStorage.DeleteAsync(book.FileKey, ct);
            await repository.RemoveAsync(book, ct);
        }
    }

    private async Task PropagateProgressAsync(Guid bookId, int? pageCount, CancellationToken ct)
    {
        var progresses = await progressRepository.ListByBookAsync(bookId, ct);
        foreach (var progress in progresses)
        {
            progress.TotalPages = pageCount;
            progress.PercentComplete = ReadingProgressCalculator.PercentComplete(progress.CurrentPage, pageCount);
            await progressRepository.UpsertAsync(progress, ct);
        }
    }

    private async Task<Book> GetAuthorizedAsync(Guid id, Guid requesterId, CancellationToken ct)
    {
        var book = await repository.GetByIdAsync(id, ct);
        if (book is null || (book.Source == BookSource.UserUpload && book.OwnerId != requesterId))
            throw new BookNotFoundException();

        return book;
    }

    private static BookScope ParseScope(string? scope) =>
        scope?.ToLowerInvariant() switch
        {
            "public" => BookScope.Public,
            "mine" => BookScope.Mine,
            "all" or null or "" => BookScope.All,
            _ => throw new BookValidationException([$"Valor de 'scope' inválido: '{scope}'."]),
        };
}
