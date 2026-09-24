using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Identity;

public class AuthException(string message) : UnauthorizedException(message);
