namespace EReader_API.Application.Identity;

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
