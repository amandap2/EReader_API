using EReader_API.Application.Common.Exceptions;

namespace EReader_API.Application.Identity;

public class IdentityValidationException(IEnumerable<string> errors) : ValidationException(errors);
