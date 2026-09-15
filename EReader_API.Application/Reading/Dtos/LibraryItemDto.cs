using EReader_API.Application.Catalog;

namespace EReader_API.Application.Reading;

public record LibraryItemDto(BookDto Book, ProgressDto? Progress);
