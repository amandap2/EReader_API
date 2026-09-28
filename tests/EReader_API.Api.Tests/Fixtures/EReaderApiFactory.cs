using System.Text;
using EReader_API.Api.Tests.Fakes;
using EReader_API.Application.Identity;
using EReader_API.Infra.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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

    private readonly string _jwtSigningKey =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("test-signing-key-32-bytes-minimum!!"));

    private Respawner? _respawner;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Program.cs.AddInfrastructure lê ConnectionStrings:Default (e o Options<JwtOptions>
        // valida Jwt:SigningKey) de forma síncrona em builder.Services.AddInfrastructure(...),
        // ANTES de builder.Build() ser chamado. O hook ConfigureWebHost/ConfigureAppConfiguration
        // abaixo só é aplicado no instante do Build() (via a interceptação por DiagnosticListener
        // que o WebApplicationFactory usa para hospedar um Program.cs de top-level statements) —
        // tarde demais para essa leitura eager. Setar variável de ambiente de processo ANTES do
        // primeiro acesso a `Services` funciona porque WebApplicationBuilder.CreateBuilder já
        // inclui variáveis de ambiente como fonte de config desde a criação do builder.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing"); // D-05-5
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _db.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__SigningKey", _jwtSigningKey); // D-05-7
        Environment.SetEnvironmentVariable("FileStorage__RootPath", _fileStorageRoot); // D-05-6

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
        // Redundante com a variável de ambiente ASPNETCORE_ENVIRONMENT acima (que já resolve
        // isso antes do Build()), mas mantido explícito: pula o bloco
        // `if (Environment.IsDevelopment())` do Program.cs (migração automática + seed do
        // catálogo público + /openapi//scalar) e, com o guard de P3, também pula
        // app.UseRateLimiter() (D-05-9) — ambos avaliados em cima de `app.Environment` depois do
        // Build(), onde este hook já surte efeito normalmente.
        builder.UseEnvironment("Testing");

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
