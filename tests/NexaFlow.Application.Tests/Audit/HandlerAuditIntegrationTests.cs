using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Tests.Auth;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Application.Tests.Audit;

/// <summary>
///     Phase 8 integration-style tests verifying that handlers correctly invoke
///     <see cref="IAuditService" /> to record audit rows. Uses the FakeAuditService
///     which captures calls; does not require a real DB.
/// </summary>
public sealed class HandlerAuditIntegrationTests
{
    [Fact]
    public async Task LoginCommandHandler_should_record_LoginSucceeded_on_successful_login()
    {
        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var audit = new FakeAuditService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<LoginCommandHandler>();
        var handler = new LoginCommandHandler(db, passwordHasher, jwt, refreshStore, audit, TestAuthOptions.Default);

        var user = NexaFlow.Domain.Entities.User.Create(
            email: Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: passwordHasher.Hash("StrongPass1!"),
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        user.VerifyEmail(DateTimeOffset.UtcNow);
        db.Users.Add(user);

        await handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None);

        // The handler should have recorded LoginSucceeded with actor = the user.
        audit.Entries.Should().Contain(e =>
            e.Action == AuditAction.LoginSucceeded &&
            e.Entity == "User" &&
            e.EntityId == user.Id &&
            e.ActorUserIdOverride == user.Id &&
            e.OrganizationIdOverride == null);
    }

    [Fact]
    public async Task LoginCommandHandler_should_record_LoginFailed_on_wrong_password()
    {
        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var audit = new FakeAuditService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<LoginCommandHandler>();
        var handler = new LoginCommandHandler(db, passwordHasher, jwt, refreshStore, audit, TestAuthOptions.Default);

        var user = NexaFlow.Domain.Entities.User.Create(
            email: Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: passwordHasher.Hash("StrongPass1!"),
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        user.VerifyEmail(DateTimeOffset.UtcNow);
        db.Users.Add(user);

        // Wrong password — handler should record LoginFailed + actor = the user
        // (we know who they are even though the password was wrong).
        var act = () => handler.Handle(
            new LoginCommand("alice@example.com", "WrongPassword!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        audit.Entries.Should().Contain(e =>
            e.Action == AuditAction.LoginFailed &&
            e.Entity == "User" &&
            e.EntityId == user.Id &&
            e.ActorUserIdOverride == user.Id);
    }

    [Fact]
    public async Task LoginCommandHandler_should_record_LoginFailed_with_no_actor_for_unknown_email()
    {
        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var audit = new FakeAuditService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<LoginCommandHandler>();
        var handler = new LoginCommandHandler(db, passwordHasher, jwt, refreshStore, audit, TestAuthOptions.Default);

        // No user seeded — login attempt for a non-existent email.
        var act = () => handler.Handle(
            new LoginCommand("nobody@example.com", "AnyPassword!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        audit.Entries.Should().Contain(e =>
            e.Action == AuditAction.LoginFailed &&
            e.EntityId == null &&
            e.ActorUserIdOverride == null);
    }

    [Fact]
    public async Task ChangePasswordCommandHandler_should_record_PasswordChanged_without_secrets()
    {
        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var cache = new FakeCacheService();
        var audit = new FakeAuditService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<ChangePasswordCommandHandler>();
        var handler = new ChangePasswordCommandHandler(
            db, passwordHasher, refreshStore, cache, audit, TestAuthOptions.Default, logger);

        var user = NexaFlow.Domain.Entities.User.Create(
            email: Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: passwordHasher.Hash("OldPassword1!"),
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        user.VerifyEmail(DateTimeOffset.UtcNow);
        db.Users.Add(user);

        await handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPassword1!", "NewStrongPass2!"),
            CancellationToken.None);

        // Audit row recorded with NO old/new values (password is a secret).
        audit.Entries.Should().Contain(e =>
            e.Action == AuditAction.PasswordChanged &&
            e.Entity == "User" &&
            e.EntityId == user.Id &&
            e.OldValues == null &&
            e.NewValues == null);
    }

    [Fact]
    public async Task LogoutCommandHandler_should_record_Logout_for_authenticated_user()
    {
        var db = new InMemoryApplicationDbContext();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var audit = new FakeAuditService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<LogoutCommandHandler>();
        var currentUser = new FakeCurrentUserService
        {
            UserId = Guid.NewGuid(),
            IsAuthenticated = true
        };
        var handler = new LogoutCommandHandler(refreshStore, db, currentUser, audit, logger);

        // Issue a refresh token so logout has something to revoke.
        var issued = await refreshStore.IssueNewAsync(
            userId: currentUser.UserId!.Value,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(7),
            atUtc: DateTimeOffset.UtcNow,
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);

        await handler.Handle(
            new LogoutCommand(issued.PlaintextToken),
            CancellationToken.None);

        audit.Entries.Should().Contain(e =>
            e.Action == AuditAction.Logout &&
            e.Entity == "User" &&
            e.EntityId == currentUser.UserId.Value);
    }
}
