using EReader_API.Application.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace EReader_API.Infra.Storage;

public class FileStorageHealthCheck(FileStorageOptions options, IHostEnvironment env) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.RootPath));
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Diretório de storage não é gravável.", ex);
        }
    }
}
