using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Application.Tests.Auth;

/// <summary>
///     Phase 2 unit tests for the <see cref="RegisterCommandHandler" />.
/// </summary>
public sealed class RegisterCommandHandlerTests
{
    private readonly InMemoryApplicationDbContext _db = new();
    private readonly FakeJwtTokenService _jwt = new();
    private readonly FakeRefreshTokenStore _refreshStore;
    private readonly FakeSecureTokenGenerator _tokenGenerator = new();
    private readonly FakeEmailService _emailService = new();
    private readonly FakeCurrentUserService _currentUser = new();

    public RegisterCommandHandlerTests()
    {
        _refreshStore = new FakeRefreshTokenStore(_tokenGenerator);
    }

    private RegisterCommandHandler CreateHandler() =>
        new(
            db: _db,
            passwordHasher: new FakePasswordHasher(),
            tokenGenerator: _tokenGenerator,
            jwtTokenService: _jwt,
            refreshTokenStore: _refreshStore,
            emailService: _emailService,
            currentUser: _currentUser,
            options: TestAuthOptions.Default,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<RegisterCommandHandler>());

    [Fact]
    public async Task Register_with_valid_input_should_create_user_and_return_tokens()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new RegisterCommand(
            Email: "alice@example.com",
            DisplayName: "Alice",
            Password: "StrongPass1!");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.AccessTokenExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        result.RefreshTokenExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        result.User.Email.Should().Be("alice@example.com");
        result.User.EmailVerified.Should().BeFalse();

        _db.Users.Should().ContainSingle();
        _refreshStore.Tokens.Should().ContainSingle();
        _emailService.SentEmails.Should().ContainSingle();
    }

    [Fact]
    public async Task Register_with_duplicate_email_should_throw_domain_exception()
    {
        // Arrange — pre-populate a user
        var existing = NexaFlow.Domain.Entities.User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Existing Alice",
            passwordHash: "hash",
            emailVerificationTokenHash: "verify-hash",
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            createdAtUtc: DateTimeOffset.UtcNow);
        _db.Users.Add(existing);

        var handler = CreateHandler();
        var command = new RegisterCommand(
            Email: "Alice@Example.com", // case-insensitive collision on normalized
            DisplayName: "Alice 2",
            Password: "StrongPass1!");

        // Act + Assert
        var act = () => handler.Handle(command, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Register_should_hash_password_not_store_plaintext()
    {
        // Arrange
        var handler = CreateHandler();
        var password = "StrongPass1!";

        // Act
        var result = await handler.Handle(
            new RegisterCommand("alice@example.com", "Alice", password),
            CancellationToken.None);

        // Assert — stored hash is NOT the plaintext password.
        var storedUser = _db.Users[0];
        storedUser.PasswordHash.Should().NotBe(password);
        storedUser.PasswordHash.Should().NotBeNullOrEmpty();
    }
}

// --- Additional small test doubles used above ---

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string plaintext) => $"hash-of:{plaintext}";
    public bool Verify(string plaintext, string storedHash) => storedHash == $"hash-of:{plaintext}";
}

internal sealed class FakeEmailService : IEmailService
{
    public List<EmailRequest> SentEmails { get; } = [];

    public Task SendAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        SentEmails.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? IPAddress { get; set; } = "127.0.0.1";
    public string? TraceId { get; set; } = "test-trace";
}
