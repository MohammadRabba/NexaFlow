using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Behaviors;

namespace NexaFlow.Application;

/// <summary>
///     DI entry point for the Application layer. The API host calls this in
///     <c>Program.cs</c> to register MediatR, FluentValidation, the pipeline
///     behaviors, and the <see cref="AuthOptions" /> configuration binding.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // MediatR 12.x: register by assembly scanning the Application layer.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            // TransactionBehavior will be added when transactions are first needed.
        });

        // FluentValidation — auto-register all IValidator<> implementations in the Application layer.
        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);

        // Phase 4: shared project-access helper used by the project mutation handlers.
        // Stateless function-bag — registered as a singleton, but each handler gets its own
        // scoped dependencies via constructor injection.
        services.AddScoped<Features.Projects.Commands.ProjectAccess>();

        return services;
    }

    /// <summary>
    ///     Bind the Authentication configuration section into <see cref="AuthOptions" />.
    ///     The actual values come from appsettings.json (Development) or environment
    ///     variables (Production). The signing key is loaded once at startup.
    /// </summary>
    public static IServiceCollection AddAuthOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Authentication"))
            .ValidateDataAnnotations()
            .Validate(opts =>
                !string.IsNullOrWhiteSpace(opts.JwtSigningKey) &&
                opts.JwtSigningKey.Length >= 32,
                "Authentication:JwtSigningKey must be at least 32 characters (256 bits).")
            .ValidateOnStart();

        return services;
    }
}
