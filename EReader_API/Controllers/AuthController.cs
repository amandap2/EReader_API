using EReader_API.Application.Common;
using EReader_API.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(req, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct) =>
        Ok(await authService.LoginAsync(req, ct));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req, CancellationToken ct) =>
        Ok(await authService.RefreshAsync(req.RefreshToken, ct));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest req, CancellationToken ct)
    {
        await authService.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
    {
        await authService.ForgotPasswordAsync(req.Email, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await authService.ResetPasswordAsync(req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await authService.GetProfileAsync(User.GetUserId(), ct));

    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        await authService.DeleteAccountAsync(User.GetUserId(), ct);
        return NoContent();
    }
}
