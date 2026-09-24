using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Catalog;

public class BookValidationException(IEnumerable<string> errors) : ValidationException(errors);
