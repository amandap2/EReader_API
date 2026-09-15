using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record LogoutRequest([Required] string RefreshToken);
