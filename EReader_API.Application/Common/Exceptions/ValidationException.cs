namespace EReader_API.Application.Common.Exceptions;

public abstract class ValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
