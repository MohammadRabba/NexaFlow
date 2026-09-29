using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Users;

/// <summary>
///     Raised when refresh-token reuse is detected — i.e., a revoked (already-rotated)
///     token was presented again. This is a security signal: the entire token family
///     is revoked to defend against token-theft replay attacks. Future phases will
///     publish this to an audit / security feed via the outbox.
/// </summary>
public sealed record RefreshTokenReuseDetectedEvent(
    Guid UserId,
    Guid TokenFamilyId,
    Guid PresentedTokenId,
    string? PresentedFromIp,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
