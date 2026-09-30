using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Application.Tests.Auth;

/// <summary>
///     Phase 2 unit tests for the <see cref="LoginCommandHandler" /> — covering
///     valid credentials, invalid password, nonexistent account, unverified email,
///     locked account, failed-attempt counting, and successful-login counter reset.
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private readonly InMemoryApplicationDbContext _db = new();
    private readonly FakeJwtTokenService _jwt = new();
    private readonly FakeRefreshTokenStore _refreshStore;
    private readonly FakePasswordHasher _passwordHasher = new();
    private readonly FakeSecureTokenGenerator _tokenGenerator = new();
    private readonly FakeCurrentUserService _currentUser = new();
    private readonly FakeAuditService _audit = new();

    public LoginCommandHandlerTests()
    {
        _refreshStore = new FakeRefreshTokenStore(_tokenGenerator);
    }

    private LoginCommandHandler CreateHandler() =>
        new(
            db: _db,
            passwordHasher: _passwordHasher,
            jwtTokenService: _jwt,
            refreshTokenStore: _refreshStore,
            audit: _audit,
            options: TestAuthOptions.Default);

    private void SeedVerifiedUser(string email = "alice@example.com", string password = "StrongPass1!")
    {
        var user = NexaFlow.Domain.Entities.User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create(email),
            displayName: "Alice",
            passwordHash: _passwordHasher.Hash(password),
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        // Mark email as verified directly — this test is about login, not verification.
        user.VerifyEmail(DateTimeOffset.UtcNow);
        _db.Users.Add(user);
    }

    [Fact]
    public async Task Login_with_valid_credentials_should_return_tokens()
    {
        // Arrange
        SeedVerifiedUser(password: "StrongPass1!");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None);

        // Assert
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("alice@example.com");
        result.User.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Login_with_invalid_password_should_throw_INVALID_CREDENTIALS_and_increment_failures()
    {
        // Arrange
        SeedVerifiedUser(password: "StrongPass1!");
        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new LoginCommand("alice@example.com", "WrongPassword!"),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_CREDENTIALS");

        // The failed-login counter should have been incremented.
        var storedUser = _db.Users[0];
        storedUser.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_with_nonexistent_account_should_throw_INVALID_CREDENTIALS()
    {
        // Arrange — no user seeded
        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new LoginCommand("nobody@example.com", "AnyPassword1!"),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        // The error message must NOT reveal that the account doesn't exist.
        ex.Which.Message.Should().NotContain("not found");
        ex.Which.Message.Should().NotContain("does not exist");
        ex.Which.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_with_unverified_email_should_throw_EMAIL_NOT_VERIFIED()
    {
        // Arrange — seed an UNVERIFIED user
        var user = NexaFlow.Domain.Entities.User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: _passwordHasher.Hash("StrongPass1!"),
            emailVerificationTokenHash: "verify-hash",
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            createdAtUtc: DateTimeOffset.UtcNow);
        _db.Users.Add(user);

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task Login_with_locked_account_should_throw_ACCOUNT_LOCKED()
    {
        // Arrange — seed a verified user and force-lockout by registering N failed attempts
        SeedVerifiedUser(password: "StrongPass1!");
        var user = _db.Users[0];
        // Force-lockout: register MaxFailedLoginAttempts failures.
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow.AddSeconds(i));
        }
        user.IsLockedOut.Should().BeTrue();

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task Failed_attempt_count_should_increment_with_each_failed_login()
    {
        // Arrange
        SeedVerifiedUser(password: "StrongPass1!");
        var handler = CreateHandler();

        // Act — 3 failed attempts
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
                new LoginCommand("alice@example.com", "WrongPassword1!"),
                CancellationToken.None));
        }

        // Assert
        var storedUser = _db.Users[0];
        storedUser.FailedLoginAttempts.Should().Be(3);
        storedUser.IsLockedOut.Should().BeFalse(); // below threshold of 5
    }

    [Fact]
    public async Task Successful_login_should_reset_failed_attempt_counter()
    {
        // Arrange
        SeedVerifiedUser(password: "StrongPass1!");
        var user = _db.Users[0];
        // Register 2 prior failures
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow);
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow.AddSeconds(1));

        var handler = CreateHandler();

        // Act — successful login with correct password
        var result = await handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None);

        // Assert
        result.AccessToken.Should().NotBeNullOrEmpty();
        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut.Should().BeFalse();
    }
}
