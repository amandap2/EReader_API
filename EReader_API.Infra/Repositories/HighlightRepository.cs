using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class HighlightRepository(ApplicationDbContext db) : IHighlightRepository
{
    public async Task<IReadOnlyList<Highlight>> ListAsync(Guid userId, Guid bookId, CancellationToken ct) =>
        await db.Highlights.Where(h => h.UserId == userId && h.BookId == bookId).ToListAsync(ct);

    public Task<Highlight?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Highlights.FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task AddAsync(Highlight highlight, CancellationToken ct)
    {
        db.Highlights.Add(highlight);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Highlight highlight, CancellationToken ct)
    {
        db.Highlights.Update(highlight);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Highlight highlight, CancellationToken ct)
    {
        db.Highlights.Remove(highlight);
        await db.SaveChangesAsync(ct);
    }
}
