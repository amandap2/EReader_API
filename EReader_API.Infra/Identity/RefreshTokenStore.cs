using System.Security.Cryptography;
using System.Text;
using EReader_API.Application.Identity;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EReader_API.Infra.Identity
{
    public class RefreshTokenStore(ApplicationDbContext db, IOptions<JwtOptions> options) : IRefreshTokenStore
    {
        public async Task<string> CreateAsync(Guid userId, CancellationToken ct)
        {
            var token = GenerateOpaqueToken();

            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = Hash(token),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(options.Value.RefreshTokenDays),
            });

            await db.SaveChangesAsync(ct);
            return token;
        }

        public async Task<StoredRefreshToken?> FindByTokenAsync(string refreshToken, CancellationToken ct)
        {
            var hash = Hash(refreshToken);
            var stored = await db.RefreshTokens.AsNoTracking()
                .SingleOrDefaultAsync(rt => rt.TokenHash == hash, ct);

            if (stored is null)
                return null;

            return new StoredRefreshToken(stored.UserId, stored.IsActive, stored.RevokedAt is not null);
        }

        public async Task RevokeAsync(string refreshToken, string? replacedByToken, CancellationToken ct)
        {
            var hash = Hash(refreshToken);
            var stored = await db.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == hash, ct);
            if (stored is null)
                return;

            stored.RevokedAt = DateTime.UtcNow;
            stored.ReplacedByTokenHash = replacedByToken is null ? null : Hash(replacedByToken);
            await db.SaveChangesAsync(ct);
        }

        public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
        {
            await db.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.RevokedAt, DateTime.UtcNow), ct);
        }

        private static string GenerateOpaqueToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string Hash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToBase64String(bytes);
        }
    }
}
