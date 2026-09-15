using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);
