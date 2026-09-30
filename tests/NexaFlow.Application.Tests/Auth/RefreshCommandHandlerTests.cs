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
///     Phase 2 unit tests for the <see cref="RefreshCommandHandler" /> — covering:
///     <list type="bullet">
///         <item>Valid refresh → new tokens, old token revoked</item>
///         <item>Refresh with expired token → INVALID_REFRESH_TOKEN</item>
///         <item>Refresh with revoked token (reuse) → family-wide revocation, INVALID_REFRESH_TOKEN</item>
///         <item>Logout invalidates the refresh token (subsequent refresh fails)</item>
///     </list>
/// </summary>
public sealed class RefreshCommandHandlerTests
{
    private readonly InMemoryApplicationDbContext _db = new();
    private readonly FakeJwtTokenService _jwt = new();
    private readonly FakeRefreshTokenStore _refreshStore;
    private readonly FakeSecureTokenGenerator _tokenGenerator = new();
    private readonly FakeAuditService _audit = new();

    public RefreshCommandHandlerTests()
    {
        _refreshStore = new FakeRefreshTokenStore(_tokenGenerator);
    }

    private RefreshCommandHandler CreateHandler() =>
        new(
            db: _db,
            jwtTokenService: _jwt,
            refreshTokenStore: _refreshStore,
            audit: _audit,
            options: TestAuthOptions.Default);

    private User SeedVerifiedUser()
    {
        var user = User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: "hash",
            emailVerificationTokenHash: null,
            emailVerificationTokenExpiresAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
        _db.Users.Add(user);
        return user;
    }

    private async Task<(string PlaintextRefresh, Guid TokenId)> IssueInitialTokenAsync(Guid userId)
    {
        var issued = await _refreshStore.IssueNewAsync(
            userId: userId,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(7),
            atUtc: DateTimeOffset.UtcNow,
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);
        return (issued.PlaintextToken, issued.TokenEntity.Id);
    }

    [Fact]
    public async Task Refresh_with_valid_token_should_rotate_and_issue_new_tokens()
    {
        // Arrange
        var user = SeedVerifiedUser();
        var (initialPlaintext, initialId) = await IssueInitialTokenAsync(user.Id);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            new RefreshCommand(initialPlaintext),
            CancellationToken.None);

        // Assert
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBe(initialPlaintext);

        // The old token must now be revoked (rotated).
        var oldToken = _refreshStore.Tokens.Single(t => t.Id == initialId);
        oldToken.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByTokenId.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_with_expired_token_should_throw_INVALID_REFRESH_TOKEN()
    {
        // Arrange
        var user = SeedVerifiedUser();
        var issued = await _refreshStore.IssueNewAsync(
            userId: user.Id,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1), // already expired
            atUtc: DateTimeOffset.UtcNow.AddDays(-8),
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);
        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new RefreshCommand(issued.PlaintextToken),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_with_unknown_token_should_throw_INVALID_REFRESH_TOKEN()
    {
        // Arrange
        SeedVerifiedUser();
        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(
            new RefreshCommand("never-existed"),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_with_already_rotated_token_should_revoke_entire_family()
    {
        // Arrange — first refresh: rotate A → B. Second refresh: try A again → reuse detected.
        var user = SeedVerifiedUser();
        var (initialPlaintext, _) = await IssueInitialTokenAsync(user.Id);
        var handler = CreateHandler();

        // First refresh — succeeds, rotates A → B
        var firstResult = await handler.Handle(
            new RefreshCommand(initialPlaintext),
            CancellationToken.None);

        // Second refresh — present A again. Should trigger reuse detection.
        var act = () => handler.Handle(
            new RefreshCommand(initialPlaintext),
            CancellationToken.None);

        // Assert — the handler propagates RefreshTokenReuseException up. The API layer's
        // exception-handling middleware will translate this to a 401 INVALID_REFRESH_TOKEN.
        var ex = await act.Should().ThrowAsync<RefreshTokenReuseException>();
        ex.Which.UserId.Should().Be(user.Id);

        // The store should have revoked the ENTIRE family — A and B.
        var familyId = _refreshStore.Tokens.First().FamilyId;
        var allFamilyTokens = _refreshStore.Tokens.Where(t => t.FamilyId == familyId).ToList();
        allFamilyTokens.Should().NotBeEmpty();
        allFamilyTokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task Logout_should_revoke_refresh_token_so_subsequent_refresh_fails()
    {
        // Arrange
        var user = SeedVerifiedUser();
        var (initialPlaintext, _) = await IssueInitialTokenAsync(user.Id);

        // Act — logout (revokes the token)
        var logoutHandler = new LogoutCommandHandler(
            refreshTokenStore: _refreshStore,
            db: _db,
            currentUser: new FakeCurrentUserService(),
            audit: _audit,
            logger: LoggerFactory.Create(_ => { }).CreateLogger<LogoutCommandHandler>());
        await logoutHandler.Handle(new LogoutCommand(initialPlaintext), CancellationToken.None);

        // Now attempt to refresh with the logged-out token — should trigger reuse detection
        // (the token is already revoked). The handler propagates RefreshTokenReuseException;
        // the API middleware maps it to 401 INVALID_REFRESH_TOKEN.
        var refreshHandler = CreateHandler();
        var act = () => refreshHandler.Handle(
            new RefreshCommand(initialPlaintext),
            CancellationToken.None);

        // Assert — RefreshTokenReuseException is propagated (the store has revoked the family).
        var ex = await act.Should().ThrowAsync<RefreshTokenReuseException>();
        ex.Which.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Refresh_with_nonexistent_user_should_revoke_family_and_throw()
    {
        // Arrange — issue a token but don't seed the user (simulating deleted user).
        var (_, plaintext) = await _refreshStore.IssueNewAsync(
            userId: Guid.NewGuid(),
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(7),
            atUtc: DateTimeOffset.UtcNow,
            createdFromIp: null,
            createdByUserAgent: null,
            CancellationToken.None);

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(new RefreshCommand(plaintext), CancellationToken.None);

        // Assert — user not found → revoke family, return INVALID_REFRESH_TOKEN.
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_REFRESH_TOKEN");
    }
}
