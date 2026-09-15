using EReader_API.Application.Catalog;
using EReader_API.Application.Reading;
using Microsoft.Extensions.DependencyInjection;

namespace EReader_API.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IBookService, BookService>();

        services.AddScoped<IReadingProgressService, ReadingProgressService>();
        services.AddScoped<IBookmarkService, BookmarkService>();
        services.AddScoped<IHighlightService, HighlightService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ILibraryService, LibraryService>();

        return services;
    }
}
