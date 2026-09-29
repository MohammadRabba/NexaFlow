using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IEmailService _emailService;
    private readonly AuthOptions _options;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IApplicationDbContext db,
        ISecureTokenGenerator tokenGenerator,
        IEmailService emailService,
        IOptions<AuthOptions> options,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _db = db;
        _tokenGenerator = tokenGenerator;
        _emailService = emailService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        var email = NexaFlow.Domain.ValueObjects.Email.Create(request.Email);
        var normalizedEmail = email.Normalized;

        var user = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Section 8 — do not reveal whether the email exists. The endpoint returns 200
            // regardless; nothing is sent for unknown emails.
            _logger.LogInformation(
                "Password reset requested for unrecognized email at {AtUtc} (no email sent).",
                now);
            return Unit.Value;
        }

        // Issue a fresh password reset token — single use, hashed only.
        var (tokenPlaintext, tokenHash) = _tokenGenerator.Generate();
        var expiresAt = now + _options.PasswordResetTokenLifetime;
        user.IssuePasswordResetToken(tokenHash, expiresAt, now);

        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort email delivery — the token is in the DB.
        try
        {
            await _emailService.SendAsync(new EmailRequest(
                To: user.Email.Value,
                Subject: "Reset your NexaFlow password",
                HtmlBody: $"<p>Use this password reset token: <code>{tokenPlaintext}</code></p>" +
                          $"<p>The token expires in {Math.Round(_options.PasswordResetTokenLifetime.TotalHours)} hour(s). " +
                          "If you did not request a password reset, ignore this email.</p>",
                TextBody: $"Reset token: {tokenPlaintext}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send password reset email for user {UserId} at {AtUtc}. " +
                "User can retry the forgot-password endpoint.",
                user.Id,
                now);
        }

        return Unit.Value;
    }
}
