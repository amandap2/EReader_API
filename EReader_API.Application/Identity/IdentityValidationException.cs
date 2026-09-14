namespace EReader_API.Application.Identity;

public class IdentityValidationException(IEnumerable<string> errors) : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
