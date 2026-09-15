namespace EReader_API.Application.Common.Exceptions;

public abstract class UnauthorizedException(string message) : Exception(message);
