using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Catalog;

public class BookNotFoundException() : NotFoundException("Livro não encontrado.");
