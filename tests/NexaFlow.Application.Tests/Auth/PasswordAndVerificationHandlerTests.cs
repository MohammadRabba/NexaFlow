using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;
using Xunit;

namespace NexaFlow.Application.Tests.Auth;

/// <summary>
///     Phase 2 unit tests for password reset + email verification + change password handlers.
/// </summary>
public sealed class PasswordAndVerificationHandlerTests
{
    private readonly InMemoryApplicationDbContext _db = new();
    private readonly FakePasswordHasher _passwordHasher = new();
    private readonly FakeSecureTokenGenerator _tokenGenerator = new();
    private readonly FakeRefreshTokenStore _refreshStore;
    private readonly FakeEmailService _emailService = new();
    private readonly FakeAuditService _audit = new();

    public PasswordAndVerificationHandlerTests()
    {
        _refreshStore = new FakeRefreshTokenStore(_tokenGenerator);
    }

    private User SeedVerifiedUser(string password = "StrongPass1!",
                                    string email = "alice@example.com")
    {
        var user = User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create(email),
            displayName: "Alice",
            passwordHash: _passwordHasher.Hash(password),
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        // Mark email as verified directly — these tests aren't about the verification flow.
        user.VerifyEmail(DateTimeOffset.UtcNow);
        _db.Users.Add(user);
        return user;
    }

    // --- ForgotPassword ---

    [Fact]
    public async Task ForgotPassword_with_existing_email_should_set_a_token_and_send_email()
    {
        // Arrange
        SeedVerifiedUser();
        var handler = new ForgotPasswordCommandHandler(
            db: _db,
            tokenGenerator: _tokenGenerator,
            emailService: _emailService,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ForgotPasswordCommandHandler>());

        // Act
        await handler.Handle(
            new ForgotPasswordCommand("alice@example.com"),
            CancellationToken.None);

        // Assert — a reset token is set + an email was sent.
        var user = _db.Users[0];
        user.PasswordResetTokenHash.Should().NotBeNullOrEmpty();
        user.PasswordResetTokenExpiresAtUtc.Should().NotBeNull();
        _emailService.SentEmails.Should().ContainSingle();
    }

    [Fact]
    public async Task ForgotPassword_with_unknown_email_should_succeed_silently_and_send_no_email()
    {
        // Arrange — no user seeded
        var handler = new ForgotPasswordCommandHandler(
            db: _db,
            tokenGenerator: _tokenGenerator,
            emailService: _emailService,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ForgotPasswordCommandHandler>());

        // Act
        await handler.Handle(
            new ForgotPasswordCommand("nobody@example.com"),
            CancellationToken.None);

        // Assert — no exception (Section 8: do not reveal whether the email exists).
        _emailService.SentEmails.Should().BeEmpty();
    }

    // --- ResetPassword ---

    [Fact]
    public async Task ResetPassword_with_valid_token_should_change_password_and_revoke_tokens()
    {
        // Arrange
        var user = SeedVerifiedUser(password: "OldPassword1!");
        // Issue a reset token via the user aggregate.
        var (plaintext, hash) = _tokenGenerator.Generate();
        user.IssuePasswordResetToken(hash, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
        // Issue a refresh token that should be revoked after reset.
        await _refreshStore.IssueNewAsync(
            userId: user.Id,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(7),
            atUtc: DateTimeOffset.UtcNow,
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);

        var handler = new ResetPasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            tokenGenerator: _tokenGenerator,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ResetPasswordCommandHandler>());

        // Act
        await handler.Handle(
            new ResetPasswordCommand("alice@example.com", plaintext, "NewStrongPass2!"),
            CancellationToken.None);

        // Assert
        user.PasswordHash.Should().Be(_passwordHasher.Hash("NewStrongPass2!"));
        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordChangedAtUtc.Should().NotBeNull();

        // All refresh tokens (stored in the fake store) for the user should be revoked.
        _refreshStore.Tokens.Should().NotBeEmpty();
        _refreshStore.Tokens
            .Where(t => t.UserId == user.Id)
            .Should()
            .AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task ResetPassword_with_unknown_email_should_throw_INVALID_RESET_TOKEN()
    {
        // Arrange — no user
        var handler = new ResetPasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            tokenGenerator: _tokenGenerator,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ResetPasswordCommandHandler>());

        // Act
        var act = () => handler.Handle(
            new ResetPasswordCommand("nobody@example.com", "any-token", "NewStrongPass2!"),
            CancellationToken.None);

        // Assert — Section 8: don't reveal the email doesn't exist; reuse the same error.
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task ResetPassword_with_expired_token_should_throw_INVALID_RESET_TOKEN()
    {
        // Arrange — issue a reset token that already expired
        var user = SeedVerifiedUser();
        var (plaintext, hash) = _tokenGenerator.Generate();
        user.IssuePasswordResetToken(hash, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(-2));

        var handler = new ResetPasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            tokenGenerator: _tokenGenerator,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ResetPasswordCommandHandler>());

        // Act
        var act = () => handler.Handle(
            new ResetPasswordCommand("alice@example.com", plaintext, "NewStrongPass2!"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task ResetPassword_with_reused_token_should_throw_INVALID_RESET_TOKEN()
    {
        // Arrange
        var user = SeedVerifiedUser();
        var (plaintext, hash) = _tokenGenerator.Generate();
        user.IssuePasswordResetToken(hash, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        var handler = new ResetPasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            tokenGenerator: _tokenGenerator,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ResetPasswordCommandHandler>());

        // Act — first reset should succeed (consumes the token).
        await handler.Handle(
            new ResetPasswordCommand("alice@example.com", plaintext, "NewStrongPass2!"),
            CancellationToken.None);

        // Act — second reset with the SAME token should fail (single-use).
        var act = () => handler.Handle(
            new ResetPasswordCommand("alice@example.com", plaintext, "AnotherStrongPass3!"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task ResetPassword_with_invalid_token_should_throw_INVALID_RESET_TOKEN()
    {
        // Arrange
        SeedVerifiedUser();
        var handler = new ResetPasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            tokenGenerator: _tokenGenerator,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ResetPasswordCommandHandler>());

        // Act
        var act = () => handler.Handle(
            new ResetPasswordCommand("alice@example.com", "wrong-token", "NewStrongPass2!"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_RESET_TOKEN");
    }

    // --- VerifyEmail ---

    [Fact]
    public async Task VerifyEmail_with_valid_token_should_verify_email_and_clear_token()
    {
        // Arrange — seed an UNVERIFIED user with a known verification token.
        var (plaintext, hash) = _tokenGenerator.Generate();
        var user = User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: "hash",
            emailVerificationTokenHash: hash,
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            createdAtUtc: DateTimeOffset.UtcNow);
        _db.Users.Add(user);

        var handler = new VerifyEmailCommandHandler(
            db: _db,
            tokenGenerator: _tokenGenerator,
            cache: new FakeCacheService(),
            audit: _audit,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<VerifyEmailCommandHandler>());

        // Act
        await handler.Handle(
            new VerifyEmailCommand("alice@example.com", plaintext),
            CancellationToken.None);

        // Assert
        user.EmailVerified.Should().BeTrue();
        user.EmailVerificationTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task VerifyEmail_with_invalid_token_should_throw()
    {
        // Arrange
        var user = User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: "hash",
            emailVerificationTokenHash: "real-hash",
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            createdAtUtc: DateTimeOffset.UtcNow);
        _db.Users.Add(user);

        var handler = new VerifyEmailCommandHandler(
            db: _db,
            tokenGenerator: _tokenGenerator,
            cache: new FakeCacheService(),
            audit: _audit,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<VerifyEmailCommandHandler>());

        // Act
        var act = () => handler.Handle(
            new VerifyEmailCommand("alice@example.com", "wrong-token"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_VERIFICATION_TOKEN");
    }

    [Fact]
    public async Task VerifyEmail_with_expired_token_should_throw()
    {
        // Arrange — issue a verification token that already expired.
        var (plaintext, hash) = _tokenGenerator.Generate();
        var user = User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: "hash",
            emailVerificationTokenHash: hash,
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(-1), // expired
            createdAtUtc: DateTimeOffset.UtcNow.AddHours(-2));
        _db.Users.Add(user);

        var handler = new VerifyEmailCommandHandler(
            db: _db,
            tokenGenerator: _tokenGenerator,
            cache: new FakeCacheService(),
            audit: _audit,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<VerifyEmailCommandHandler>());

        var act = () => handler.Handle(
            new VerifyEmailCommand("alice@example.com", plaintext),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_VERIFICATION_TOKEN");
    }

    // --- ChangePassword ---

    [Fact]
    public async Task ChangePassword_with_correct_current_password_should_change_and_revoke_refresh_tokens()
    {
        // Arrange
        var user = SeedVerifiedUser(password: "OldPassword1!");
        await _refreshStore.IssueNewAsync(
            userId: user.Id,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(7),
            atUtc: DateTimeOffset.UtcNow,
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);

        var handler = new ChangePasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ChangePasswordCommandHandler>());

        // Act
        await handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPassword1!", "NewStrongPass2!"),
            CancellationToken.None);

        // Assert
        user.PasswordHash.Should().Be(_passwordHasher.Hash("NewStrongPass2!"));
        user.PasswordChangedAtUtc.Should().NotBeNull();
        // All refresh tokens (stored in the fake store) should be revoked.
        _refreshStore.Tokens.Should().NotBeEmpty();
        _refreshStore.Tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task ChangePassword_with_wrong_current_password_should_throw_INVALID_CURRENT_PASSWORD()
    {
        // Arrange
        var user = SeedVerifiedUser(password: "OldPassword1!");

        var handler = new ChangePasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ChangePasswordCommandHandler>());

        // Act
        var act = () => handler.Handle(
            new ChangePasswordCommand(user.Id, "WrongPassword!", "NewStrongPass2!"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_CURRENT_PASSWORD");
        // The password must NOT have been changed.
        user.PasswordHash.Should().Be(_passwordHasher.Hash("OldPassword1!"));
    }

    [Fact]
    public async Task ChangePassword_for_nonexistent_user_should_throw_INVALID_CURRENT_PASSWORD()
    {
        // Arrange — no user
        var handler = new ChangePasswordCommandHandler(
            db: _db,
            passwordHasher: _passwordHasher,
            refreshTokenStore: _refreshStore,
            cache: new FakeCacheService(),
            audit: _audit,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<ChangePasswordCommandHandler>());

        // Act — Section 8: don't reveal the user doesn't exist.
        var act = () => handler.Handle(
            new ChangePasswordCommand(Guid.NewGuid(), "anything", "NewStrongPass2!"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_CURRENT_PASSWORD");
    }
}
