using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);
