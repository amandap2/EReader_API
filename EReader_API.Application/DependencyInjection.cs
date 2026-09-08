using Microsoft.Extensions.DependencyInjection;

namespace EReader_API.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Serviços de caso de uso serão registrados aqui a partir da spec 01.
        return services;
    }
}
