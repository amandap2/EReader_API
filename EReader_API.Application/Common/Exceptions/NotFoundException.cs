namespace EReader_API.Application.Common.Exceptions;

public abstract class NotFoundException(string message) : Exception(message)
{
    protected NotFoundException() : this("Recurso não encontrado.") { }
}
