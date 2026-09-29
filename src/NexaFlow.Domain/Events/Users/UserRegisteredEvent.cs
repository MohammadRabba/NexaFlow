using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Users;

/// <summary>
///     Raised when a user registers. The plaintext email verification token is
///     NEVER included in the event payload — only the user id and the timestamp.
///     The Application layer holds the plaintext token at the time of issuance
///     and routes it through <c>IEmailService</c>; the event is for audit / outbox.
/// </summary>
public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
