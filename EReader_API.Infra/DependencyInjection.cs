using System.Threading.RateLimiting;
using EReader_API.Application.Catalog;
using EReader_API.Application.Common;
using EReader_API.Application.Identity;
using EReader_API.Application.Storage;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using EReader_API.Infra.Identity;
using EReader_API.Infra.Pdf;
using EReader_API.Infra.Repositories;
using EReader_API.Infra.Seed;
using EReader_API.Infra.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EReader_API.Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'Default' não configurada (ConnectionStrings:Default / ConnectionStrings__Default).");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey não configurado.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()!;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy("upload", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.GetUserId().ToString(),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IEmailSender, LogEmailSender>();

        var fileStorageOptions = configuration.GetSection("FileStorage").Get<FileStorageOptions>()
                                  ?? new FileStorageOptions();
        services.AddSingleton(fileStorageOptions);
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = fileStorageOptions.MaxUploadBytes);

        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IPdfInspector, DocnetPdfInspector>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<PublicLibrarySeeder>();

        services.AddScoped<IReadingProgressRepository, ReadingProgressRepository>();
        services.AddScoped<IBookmarkRepository, BookmarkRepository>();
        services.AddScoped<IHighlightRepository, HighlightRepository>();
        services.AddScoped<INoteRepository, NoteRepository>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>()
            .AddCheck<FileStorageHealthCheck>("file_storage");

        return services;
    }
}
