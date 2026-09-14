namespace EReader_API.Application.Identity;

public record AuthResult(string AccessToken, string RefreshToken, int ExpiresInSeconds);
