using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(8)] string Password,
    [property: Required, StringLength(100)] string DisplayName);
