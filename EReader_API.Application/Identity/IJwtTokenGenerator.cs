namespace EReader_API.Application.Identity;

public interface IJwtTokenGenerator
{
    (string AccessToken, int ExpiresInSeconds) Generate(Guid userId, string email, string displayName);
}
