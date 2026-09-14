namespace EReader_API.Application.Identity;

public record UserProfile(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
