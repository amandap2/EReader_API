using EReader_API.Domain.Common;
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class BookRepository(ApplicationDbContext db) : IBookRepository
{
    public Task<Book?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<Book>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        await db.Books.Where(b => ids.Contains(b.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<Book>> GetOwnedByUserAsync(Guid ownerId, CancellationToken ct) =>
        await db.Books.Where(b => b.OwnerId == ownerId).ToListAsync(ct);

    public async Task<PagedResult<Book>> QueryAsync(BookQuery query, CancellationToken ct)
    {
        IQueryable<Book> books = db.Books.AsNoTracking();

        books = query.Scope switch
        {
            BookScope.Public => books.Where(b => b.Source == BookSource.PublicDomain),
            BookScope.Mine => books.Where(b => b.OwnerId == query.RequesterId),
            _ => books.Where(b => b.Source == BookSource.PublicDomain || b.OwnerId == query.RequesterId),
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search}%";
            books = books.Where(b => EF.Functions.ILike(b.Title, pattern) ||
                                      (b.Author != null && EF.Functions.ILike(b.Author, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            var pattern = $"%{query.Author}%";
            books = books.Where(b => b.Author != null && EF.Functions.ILike(b.Author, pattern));
        }

        books = query.Sort switch
        {
            "-title" => books.OrderByDescending(b => b.Title),
            "author" => books.OrderBy(b => b.Author),
            "-author" => books.OrderByDescending(b => b.Author),
            "createdAt" => books.OrderBy(b => b.CreatedAt),
            "-createdAt" => books.OrderByDescending(b => b.CreatedAt),
            _ => books.OrderBy(b => b.Title),
        };

        var totalCount = await books.CountAsync(ct);
        var items = await books
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Book>(items, totalCount, query.Page, query.PageSize);
    }

    public async Task AddAsync(Book book, CancellationToken ct)
    {
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Book book, CancellationToken ct)
    {
        db.Books.Update(book);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Book book, CancellationToken ct)
    {
        db.Books.Remove(book);
        await db.SaveChangesAsync(ct);
    }
}
