using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record ForgotPasswordRequest([Required, EmailAddress] string Email);
