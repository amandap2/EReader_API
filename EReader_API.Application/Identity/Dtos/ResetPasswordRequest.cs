using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword);
