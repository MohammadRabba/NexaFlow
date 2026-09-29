using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Result of issuing a new refresh token. The <see cref="PlaintextToken" /> is the
///     one-time-only value returned to the client; it is never persisted and never logged.
///     <see cref="TokenEntity" /> is the persisted aggregate (with only the hash stored).
/// </summary>
public sealed record IssuedRefreshToken(
    RefreshToken TokenEntity,
    string PlaintextToken);
