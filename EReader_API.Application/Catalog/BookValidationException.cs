namespace EReader_API.Application.Catalog;

public class BookValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
