using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, StringLength(100)] string DisplayName);
