namespace EReader_API.Application.Identity;

public record RegisterRequest(string Email, string Password, string DisplayName);
