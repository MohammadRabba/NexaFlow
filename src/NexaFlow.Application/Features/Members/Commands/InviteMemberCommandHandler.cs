using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Members.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Members.Commands;

public sealed class InviteMemberCommandHandler : IRequestHandler<InviteMemberCommand, InviteSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly AuthOptions _options;
    private readonly ILogger<InviteMemberCommandHandler> _logger;

    public InviteMemberCommandHandler(
        IApplicationDbContext db,
        ISecureTokenGenerator tokenGenerator,
        IEmailService emailService,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        IOptions<AuthOptions> options,
        ILogger<InviteMemberCommandHandler> logger)
    {
        _db = db;
        _tokenGenerator = tokenGenerator;
        _emailService = emailService;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InviteSummaryDto> Handle(InviteMemberCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } inviterId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Cross-tenant guard: the URL's organizationId MUST match the resolved tenant.
        // Without this check, an Admin in orgA could invite a member to orgB by simply
        // changing the URL — the [Authorize] policy only checks the resolved tenant (orgA).
        _currentTenant.EnsureMatchesTenantId(request.OrganizationId);

        // Load the organization with its members — we need to check the membership invariants
        // (e.g., "is this user already a member") on the aggregate boundary.
        var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        // Look up the invitee user by email.
        var email = Email.Create(request.InviteeEmail);
        var normalizedEmail = email.Normalized;
        var invitee = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        if (invitee is null)
        {
            // Section 8 — don't reveal whether the email exists. For invitations, however,
            // we DO need to fail because there's no user to invite. We throw a 404 with a
            // generic "user not found for this email" message that's distinguishable from
            // the cross-tenant leak-avoidance 404 (which is "resource not found").
            throw new DomainException(
                "No registered user with this email. Ask them to register first.",
                "INVITEE_NOT_REGISTERED");
        }

        // Prevent inviting yourself
        if (invitee.Id == inviterId)
        {
            throw new DomainException("Cannot invite yourself to the organization.", "CANNOT_INVITE_SELF");
        }

        // Check current membership via the aggregate's collection.
        if (org.Members.Any(m => m.UserId == invitee.Id))
        {
            throw new DomainException(
                $"User {invitee.Email.Value} is already a member of this organization.",
                "USER_ALREADY_MEMBER");
        }

        // Issue invitation token (hashed only — plaintext returned once).
        var (plaintextToken, tokenHash) = _tokenGenerator.Generate();
        var expiresAt = DateTimeOffset.UtcNow + _options.EmailVerificationTokenLifetime;

        // Use the domain factory — it sets IsActive=false, stores the token hash.
        var invitation = OrganizationMember.CreatePendingInvite(
            organizationId: org.Id,
            userId: invitee.Id,
            role: request.Role,
            invitationTokenHash: tokenHash,
            atUtc: DateTimeOffset.UtcNow);

        // We bypass Organization.AddMember here because the member is NOT yet active
        // (invitation pending). AddMember would set them active. The invitation flow
        // has its own state machine.
        _db.Add(invitation);
        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort email — the token is in the DB; the invitee can also accept via
        // the explicit accept endpoint if they have the token.
        try
        {
            await _emailService.SendAsync(new EmailRequest(
                To: invitee.Email.Value,
                Subject: $"Invitation to join {org.Name}",
                HtmlBody: $"<p>You've been invited to join <b>{org.Name}</b> with role {request.Role}.</p>" +
                          $"<p>Use this invitation token: <code>{plaintextToken}</code></p>" +
                          $"<p>The token expires in {Math.Round(_options.EmailVerificationTokenLifetime.TotalHours)} hour(s).</p>",
                TextBody: $"Invitation token: {plaintextToken}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send invitation email to {Email} for org {OrgId}. " +
                "Token persisted; invitee can accept via the explicit endpoint.",
                "<redacted>", org.Id);
        }

        _logger.LogInformation(
            "User {InviterId} invited user {InviteeId} to org {OrgId} with role {Role}.",
            inviterId, invitee.Id, org.Id, request.Role);

        return new InviteSummaryDto(
            OrganizationId: org.Id,
            UserId: invitee.Id,
            Token: plaintextToken,
            ExpiresAtUtc: expiresAt);
    }
}
