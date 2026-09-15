namespace EReader_API.Application.Reading;

public class ReadingValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
