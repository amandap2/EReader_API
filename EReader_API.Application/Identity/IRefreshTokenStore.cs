namespace EReader_API.Application.Identity;

public interface IRefreshTokenStore
{
    Task<string> CreateAsync(Guid userId, CancellationToken ct);
    Task<StoredRefreshToken?> FindByTokenAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, string? replacedByToken, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}

public record StoredRefreshToken(Guid UserId, bool IsActive, bool WasRevoked);
