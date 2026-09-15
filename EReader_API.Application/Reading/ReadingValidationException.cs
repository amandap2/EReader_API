using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Reading;

public class ReadingValidationException(IEnumerable<string> errors) : ValidationException(errors);
