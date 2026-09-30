using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Authorization;
using NexaFlow.Infrastructure.Authentication;
using NexaFlow.Infrastructure.Authorization;
using NexaFlow.Infrastructure.Email;
using NexaFlow.Infrastructure.Events;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.Infrastructure.Services;
using NexaFlow.Infrastructure.Workers;

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

        // Phase 6: Event serialization (stateless, singleton)
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();

        // Phase 6: RabbitMQ publisher
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.AddSingleton<IMessageBusPublisher, RabbitMqPublisher>();

        // Phase 6: Outbox processor (multi-instance safe with FOR UPDATE SKIP LOCKED)
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxProcessor>();

        // Phase 6: RabbitMQ consumer + event handlers
        services.AddScoped<IEventHandler<NexaFlow.Domain.Events.Tasks.TaskAssignedEvent>, TaskAssignedEventHandler>();
        services.AddHostedService<RabbitMqConsumer>();

        // Phase 6: Background workers
        services.Configure<WorkerOptions>(configuration.GetSection("Workers"));
        services.AddHostedService<DeadlineReminderWorker>();
        services.AddHostedService<TokenCleanupWorker>();

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

        // Authorization — Phase 3
        // Register the ASP.NET Core authorization options + the permission handler.
        // The handler queries the DB on every check (DB is authoritative; JWT carries no roles).
        services.AddAuthorization(options =>
        {
            // Build named policies for each permission so controllers can use [Authorize(Policy = "organization.read")]
            // instead of building requirements inline. The policy names mirror the permission strings.
            foreach (var permission in AllPermissions)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser()
                          .AddRequirements(new PermissionRequirement(permission)));
            }
        });
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    /// <summary>
    ///     Every permission string known to the system. Used at DI time to build named policies
    ///     for each. Add new permissions to <see cref="Permissions" /> and they're picked up here.
    /// </summary>
    private static readonly string[] AllPermissions =
    [
        Permissions.OrganizationRead,
        Permissions.OrganizationUpdate,
        Permissions.OrganizationDelete,
        Permissions.MemberRead,
        Permissions.MemberInvite,
        Permissions.MemberUpdate,
        Permissions.MemberRemove,
        Permissions.MemberTransferOwnership,
        Permissions.ProjectRead,
        Permissions.ProjectCreate,
        Permissions.ProjectUpdate,
        Permissions.ProjectDelete,
        Permissions.TaskRead,
        Permissions.TaskCreate,
        Permissions.TaskUpdate,
        Permissions.TaskDelete,
    ];
}
