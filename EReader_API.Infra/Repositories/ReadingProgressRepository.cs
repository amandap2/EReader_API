using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class ReadingProgressRepository(ApplicationDbContext db) : IReadingProgressRepository
{
    public Task<ReadingProgress?> GetAsync(Guid userId, Guid bookId, CancellationToken ct) =>
        db.ReadingProgresses.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, ct);

    public async Task<IReadOnlyList<ReadingProgress>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        await db.ReadingProgresses.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);

    public async Task<IReadOnlyList<ReadingProgress>> ListByBookAsync(Guid bookId, CancellationToken ct) =>
        await db.ReadingProgresses.AsNoTracking().Where(p => p.BookId == bookId).ToListAsync(ct);

    public async Task UpsertAsync(ReadingProgress progress, CancellationToken ct)
    {
        var existing = await db.ReadingProgresses.FirstOrDefaultAsync(p => p.Id == progress.Id, ct);
        if (existing is null)
            db.ReadingProgresses.Add(progress);
        else
            db.Entry(existing).CurrentValues.SetValues(progress);

        await db.SaveChangesAsync(ct);
    }
}
