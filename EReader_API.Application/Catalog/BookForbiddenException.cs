using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Catalog;

public class BookForbiddenException()
    : ForbiddenException("Livros do catálogo público não podem ser removidos por usuários.");
