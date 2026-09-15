using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Reading;

public class ReadingNotFoundException() : NotFoundException("Recurso não encontrado.");
