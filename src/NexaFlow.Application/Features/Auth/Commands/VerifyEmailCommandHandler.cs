using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Events.Users;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICacheService _cache;
    private readonly IAuditService _audit;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IApplicationDbContext db,
        ISecureTokenGenerator tokenGenerator,
        ICacheService cache,
        IAuditService audit,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _db = db;
        _tokenGenerator = tokenGenerator;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Unit> Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        var email = NexaFlow.Domain.ValueObjects.Email.Create(request.Email);
        var normalizedEmail = email.Normalized;

        var user = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            throw new DomainException(
                "Verification token is invalid or expired.",
                "INVALID_VERIFICATION_TOKEN");
        }

        var presentedTokenHash = _tokenGenerator.HashPlaintext(request.Token);
        var consumed = user.ConsumeEmailVerificationToken(presentedTokenHash, now);
        if (!consumed)
        {
            throw new DomainException(
                "Verification token is invalid or expired.",
                "INVALID_VERIFICATION_TOKEN");
        }

        user.AddDomainEvent(new EmailVerifiedEvent(Guid.NewGuid(), user.Id, now));

        // Audit EmailVerified — same transaction as the user state mutation.
        // No payload: the verification token itself is a credential-like secret.
        await _audit.RecordAsync(
            action: AuditAction.EmailVerified,
            entity: "User",
            entityId: user.Id,
            actorUserIdOverride: user.Id,
            organizationIdOverride: null,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Phase 7: Invalidate user cache — EmailVerified changed.
        await _cache.RemoveAsync($"user:{user.Id}", cancellationToken);

        _logger.LogInformation(
            "Email verified for user {UserId} at {AtUtc}.",
            user.Id,
            now);
        return Unit.Value;
    }
}
