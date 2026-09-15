namespace EReader_API.Domain.Entities.Catalog;

public record BookQuery(
    Guid RequesterId, BookScope Scope, string? Search, string? Author,
    int Page, int PageSize, string? Sort);
