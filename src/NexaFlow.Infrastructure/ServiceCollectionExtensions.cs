using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexaFlow.Application.Abstractions;
using NexaFlow.Infrastructure.Authentication;
using NexaFlow.Infrastructure.Email;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.Infrastructure.Services;

namespace NexaFlow.Infrastructure;

/// <summary>
///     DI entry point for Infrastructure. Called by the Api host's Program.cs.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // DbContext — Npgsql / PostgreSQL.
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "Connection string 'PostgreSQL' is missing from configuration. " +
                "Set it in appsettings.json or via environment variables.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            });
            options.UseSnakeCaseNamingConvention();

            if (environment.IsDevelopment())
            {
                options.EnableDetailedErrors();
                // Sensitive data logging is OK in Development only — never in Production.
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Ambient scoped services
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<CurrentTenantService>();
        services.AddScoped<ICurrentTenantService>(sp => sp.GetRequiredService<CurrentTenantService>());
        services.AddScoped<ITenantServiceAccessor>(sp => sp.GetRequiredService<CurrentTenantService>());

        // Unit of work
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Password hashing
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        // Token services (Phase 2)
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Refresh token store — operates against ApplicationDbContext directly.
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

        // Email service — Dev in Development, NotConfigured in Production (Phase 6 wires a real one).
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailService, DevEmailService>();
        }
        else
        {
            services.AddSingleton<IEmailService, NotConfiguredEmailService>();
        }

        return services;
    }
}
