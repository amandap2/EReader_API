using EReader_API.Application;
using EReader_API.Infra;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
}

app.UseHttpsRedirection();
app.UseCors(WebCors);
app.UseAuthorization();

app.MapControllers();

app.Run();
