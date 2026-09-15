using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record ForgotPasswordRequest([property: Required, EmailAddress] string Email);
