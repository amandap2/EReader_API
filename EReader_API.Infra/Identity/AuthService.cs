using EReader_API.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace EReader_API.Infra.Identity
{
    public class AuthService(
        UserManager<ApplicationUser> userManager,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenStore refreshTokenStore,
        IEmailSender emailSender) : IAuthService
    {
        public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = req.Email,
                Email = req.Email,
                DisplayName = req.DisplayName,
                CreatedAt = DateTime.UtcNow,
            };

            var result = await userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                throw new IdentityValidationException(result.Errors.Select(e => e.Description));

            return await IssueTokensAsync(user, ct);
        }

        public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(req.Email);
            if (user is null || !await userManager.CheckPasswordAsync(user, req.Password))
                throw new AuthException("Credenciais inválidas.");

            return await IssueTokensAsync(user, ct);
        }

        public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            var stored = await refreshTokenStore.FindByTokenAsync(refreshToken, ct);
            if (stored is null || !stored.IsActive)
            {
                if (stored is { WasRevoked: true })
                    await refreshTokenStore.RevokeAllForUserAsync(stored.UserId, ct);

                throw new AuthException("Refresh token inválido ou expirado.");
            }

            var user = await userManager.FindByIdAsync(stored.UserId.ToString())
                       ?? throw new AuthException("Usuário não encontrado.");

            var (accessToken, expiresInSeconds) = jwtTokenGenerator.Generate(user.Id, user.Email!, user.DisplayName);
            var newRefreshToken = await refreshTokenStore.CreateAsync(user.Id, ct);
            await refreshTokenStore.RevokeAsync(refreshToken, newRefreshToken, ct);

            return new AuthResult(accessToken, newRefreshToken, expiresInSeconds);
        }

        public Task LogoutAsync(string refreshToken, CancellationToken ct) =>
            refreshTokenStore.RevokeAsync(refreshToken, null, ct);

        public async Task ForgotPasswordAsync(string email, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
                return;

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(email, token, ct);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(req.Email)
                       ?? throw new IdentityValidationException(["Token ou e-mail inválido."]);

            var result = await userManager.ResetPasswordAsync(user, req.Token, req.NewPassword);
            if (!result.Succeeded)
                throw new IdentityValidationException(result.Errors.Select(e => e.Description));
        }

        public async Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(userId.ToString())
                       ?? throw new AuthException("Usuário não encontrado.");

            return new UserProfile(user.Id, user.Email!, user.DisplayName, user.CreatedAt);
        }

        public async Task DeleteAccountAsync(Guid userId, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(userId.ToString())
                       ?? throw new AuthException("Usuário não encontrado.");

            await userManager.DeleteAsync(user);
        }

        private async Task<AuthResult> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
        {
            var (accessToken, expiresInSeconds) = jwtTokenGenerator.Generate(user.Id, user.Email!, user.DisplayName);
            var refreshToken = await refreshTokenStore.CreateAsync(user.Id, ct);
            return new AuthResult(accessToken, refreshToken, expiresInSeconds);
        }
    }
}
