using EReader_API.Application.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace EReader_API.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IBookService, BookService>();
        return services;
    }
}
