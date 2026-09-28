using System.Diagnostics;
using EReader_API.Application;
using EReader_API.Infra;
using EReader_API.Infra.Context;
using EReader_API.Infra.Seed;
using EReader_API.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();

const string WebCors = "web";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCors, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// --- Pipeline ---
app.UseExceptionHandler();   // usa IProblemDetailsService => application/problem+json
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // /openapi/v1.json
    app.MapScalarApiReference();      // UI em /scalar/v1

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<PublicLibrarySeeder>().SeedAsync(default);
}

app.UseHttpsRedirection();
app.UseCors(WebCors);
app.UseAuthentication();
app.UseAuthorization();

// Sob WebApplicationFactory/TestServer, RemoteIpAddress vem null, então a policy "auth" e o
// GlobalLimiter (partition por IP) colapsariam toda a suíte de testes numa única partição
// "unknown" — pular o middleware inteiro no ambiente de teste evita isso (ver D-05-9).
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
