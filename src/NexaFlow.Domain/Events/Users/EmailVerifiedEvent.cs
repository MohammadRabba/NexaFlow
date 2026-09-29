using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Users;

/// <summary>
///     Raised when a user's email is verified (single-use token consumed successfully).
/// </summary>
public sealed record EmailVerifiedEvent(
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
