namespace EReader_API.Application.Common.Exceptions;

public abstract class ForbiddenException(string message) : Exception(message)
{
    protected ForbiddenException() : this("Operação não permitida.") { }
}
