using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Users;

/// <summary>
///     Raised when a user's password is changed (either via the authenticated
///     change-password endpoint or via the password-reset flow).
///     Application handlers use this to invalidate refresh tokens issued before
///     the password change.
/// </summary>
public sealed record PasswordChangedEvent(
    Guid UserId,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
