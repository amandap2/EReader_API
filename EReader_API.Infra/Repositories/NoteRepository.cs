using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class NoteRepository(ApplicationDbContext db) : INoteRepository
{
    public async Task<IReadOnlyList<Note>> ListAsync(Guid userId, Guid bookId, CancellationToken ct) =>
        await db.Notes.AsNoTracking().Where(n => n.UserId == userId && n.BookId == bookId).ToListAsync(ct);

    public Task<Note?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task AddAsync(Note note, CancellationToken ct)
    {
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Note note, CancellationToken ct)
    {
        db.Notes.Update(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Note note, CancellationToken ct)
    {
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
    }
}
