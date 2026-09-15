using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record RefreshRequest([property: Required] string RefreshToken);
