using Microsoft.Extensions.DependencyInjection;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction over the ambient authenticated principal, populated by the
///     Infrastructure layer (HTTP middleware for the API host; a test fixture for tests).
///     Carries the user id, the trace identifier, and a "trusted source" flag so the
///     Application layer can refuse to make decisions on unauthenticated context.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The user id of the authenticated principal, or null if anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>True when the caller is authenticated; never trust values from anonymous callers.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    ///     The optional IP address for audit logging (section 25).
    ///     May be null in non-HTTP contexts (tests, background workers).
    /// </summary>
    string? IPAddress { get; }

    /// <summary>
    ///     The trace identifier for correlation across logs / outbox / message consumers.
    /// </summary>
    string? TraceId { get; }
}

/// <summary>
///     Marker interface for a service that runs inside the request pipeline and
///     populates <see cref="ICurrentUserService" />. The Api project registers an HTTP
///     implementation; tests register a fixed one.
/// </summary>
public interface ICurrentUserServiceInitializer
{
    /// <summary>Populate <paramref name="services" /> with the current-user service for this scope.</summary>
    void Initialize(IServiceCollection services);
}
