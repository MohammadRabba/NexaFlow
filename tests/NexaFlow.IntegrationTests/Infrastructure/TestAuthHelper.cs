using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.ValueObjects;
using NexaFlow.Infrastructure.Authentication;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.Infrastructure.Services;

namespace NexaFlow.IntegrationTests.Infrastructure;

/// <summary>
///     Helpers to seed test data and issue JWT access tokens for integration tests.
///     Provides the minimum surface area needed by the cross-tenant isolation tests —
///     no abstractions, no fluent builders, just methods.
/// </summary>
public sealed class TestAuthHelper
{
    private readonly NexaFlowWebApplicationFactory _factory;

    internal TestAuthHelper(NexaFlowWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    ///     Register a user (unverified), verify their email, return the user id.
    ///     Uses the production pipeline so the user row is real (not test-bypass).
    /// </summary>
    public async Task<(Guid UserId, string Email, string Password)> RegisterUserAsync(
        string email, string password = "StrongPass1!")
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var mediator = sp.GetRequiredService<IMediator>();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var tokenGen = sp.GetRequiredService<ISecureTokenGenerator>();

        // Register via the production command — captures the verification token via email.
        var registerResp = await mediator.Send(new RegisterCommand(email, "Test User", password));
        var plaintextToken = ExtractTokenFromBody(_factory.SentEmailBodies[^1]);
        if (plaintextToken is null)
            throw new InvalidOperationException("Verification token not captured for test user.");

        // Consume the token to verify the email.
        await mediator.Send(new VerifyEmailCommand(email, plaintextToken));
        return (registerResp.User.Id, email, password);
    }

    /// <summary>
    ///     Create an organization. The current user (from testAuth context) becomes the Owner.
    ///     To make this work, we bypass the controller and call the domain directly — we set
    ///     up the current user context via the test auth service.
    /// </summary>
    public async Task<(Guid OrgId, Guid OwnerUserId)> CreateOrganizationAsync(
        string name, Guid ownerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Set the current user via the AsyncLocal — required for audit stamping.
        SetCurrentUser(ownerUserId);

        var slug = name.ToLowerInvariant().Replace(' ', '-');
        // Ensure slug uniqueness (test data may collide)
        var i = 1;
        while (await db.Organizations.AnyAsync(o => o.Slug == slug))
        {
            slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{i++}";
        }

        var org = Organization.Create(name, slug, ownerUserId, DateTimeOffset.UtcNow);
        db.Add(org);
        await db.SaveChangesAsync();
        return (org.Id, ownerUserId);
    }

    /// <summary>
    ///     Add a membership to an organization. Bypasses the InviteMember flow (no email,
    ///     no token) — directly creates an active OrganizationMember.
    /// </summary>
    public async Task AddMembershipAsync(Guid orgId, Guid userId, OrganizationRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SetCurrentUser(userId);

        var member = OrganizationMember.CreateInternal(orgId, userId, role, isActive: true, DateTimeOffset.UtcNow);
        db.Add(member);
        await db.SaveChangesAsync();
    }

    /// <summary>
    ///     Remove a membership directly from the database (bypasses the RemoveMember
    ///     handler, which has authorization checks). Used by tests that need to simulate
    ///     "user was removed" to verify immediate access loss.
    /// </summary>
    public async Task RemoveMembershipDirectAsync(Guid orgId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var member = await db.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == orgId && m.UserId == userId && m.IsActive)
            .FirstOrDefaultAsync();
        if (member is not null)
        {
            db.OrganizationMembers.Remove(member);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    ///     Issue a JWT access token for the given user. The token is signed with the same
    ///     key the API uses, so [Authorize] accepts it.
    /// </summary>
    public string IssueAccessToken(Guid userId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Load the user (the JwtTokenService.IssueAccessToken takes a User entity).
        var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId)
            ?? throw new InvalidOperationException($"User {userId} not found in test DB.");
        return jwtService.IssueAccessToken(user, DateTimeOffset.UtcNow);
    }

    /// <summary>Populate the AsyncLocal principal — needed for the audit stamping in SaveChangesAsync.</summary>
    private static void SetCurrentUser(Guid userId)
    {
        // The production CurrentUserService is AsyncLocal-backed. For tests, we call its internal setter.
        // Workaround: use reflection to invoke the SetPrincipal method.
        var method = typeof(CurrentUserService).GetMethod("SetPrincipal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null)
            throw new InvalidOperationException("CurrentUserService.SetPrincipal not found via reflection.");
        method.Invoke(null, [userId, true, "127.0.0.1", "test-trace"]);
    }

    private static string? ExtractTokenFromBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        const string startTag = "<code>";
        const string endTag = "</code>";
        var start = body.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += startTag.Length;
        var end = body.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : body[start..end].Trim();
    }
}
