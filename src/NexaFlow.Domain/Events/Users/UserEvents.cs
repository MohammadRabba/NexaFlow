using NexaFlow.Domain.Common;
using NexaFlow.Domain.Events.Organizations;

namespace NexaFlow.Domain.Events.Users;

public sealed record UserRegisteredEvent(
    Guid EventId,
    Guid UserId,
    string Email,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record EmailVerifiedEvent(
    Guid EventId,
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record PasswordChangedEvent(
    Guid EventId,
    Guid UserId,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record RefreshTokenReuseDetectedEvent(
    Guid EventId,
    Guid UserId,
    Guid TokenFamilyId,
    Guid PresentedTokenId,
    string? PresentedFromIp,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
