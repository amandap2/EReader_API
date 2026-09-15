using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class BookmarkRepository(ApplicationDbContext db) : IBookmarkRepository
{
    public async Task<IReadOnlyList<Bookmark>> ListAsync(Guid userId, Guid bookId, CancellationToken ct) =>
        await db.Bookmarks.Where(b => b.UserId == userId && b.BookId == bookId).ToListAsync(ct);

    public Task<Bookmark?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task AddAsync(Bookmark bookmark, CancellationToken ct)
    {
        db.Bookmarks.Add(bookmark);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Bookmark bookmark, CancellationToken ct)
    {
        db.Bookmarks.Remove(bookmark);
        await db.SaveChangesAsync(ct);
    }
}
