using System.Text;
using EReader_API.Api.Tests.Fakes;
using EReader_API.Application.Identity;
using EReader_API.Infra.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace EReader_API.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory + fixture do Postgres efêmero fundidos numa classe só (D-05-8): sobe o
/// container uma vez por execução da coleção, migra, e expõe ResetAsync() para o Respawn limpar
/// as tabelas entre testes.
/// </summary>
public class EReaderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    private readonly string _fileStorageRoot =
        Path.Combine(Path.GetTempPath(), "ereader-tests", Guid.NewGuid().ToString("N"));

    private Respawner? _respawner;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.MigrateAsync();

        await using var respawnConnection = new NpgsqlConnection(_db.GetConnectionString());
        await respawnConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    public async Task ResetAsync()
    {
        FakeEmailSender.CapturedTokens.Clear();

        await using var connection = new NpgsqlConnection(_db.GetConnectionString());
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // D-05-5: pula o bloco `if (Environment.IsDevelopment())` do Program.cs (migração
        // automática + seed do catálogo público + /openapi//scalar) e, com o guard de P3,
        // também pula app.UseRateLimiter() (D-05-9).
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        [
            new("ConnectionStrings:Default", _db.GetConnectionString()),
            new("Jwt:SigningKey",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("test-signing-key-32-bytes-minimum!!"))),
            new("FileStorage:RootPath", _fileStorageRoot), // D-05-6
        ]));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender, FakeEmailSender>();
        });
    }

    public new async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_fileStorageRoot))
            Directory.Delete(_fileStorageRoot, recursive: true);

        await base.DisposeAsync();
    }
}
