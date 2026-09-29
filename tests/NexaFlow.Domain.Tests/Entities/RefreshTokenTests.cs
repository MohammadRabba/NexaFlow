using FluentAssertions;
using NexaFlow.Domain.Entities;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Tests for the <see cref="RefreshToken" /> aggregate — refresh-token rotation,
///     revocation, and the critical reuse-detection invariant.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Expiry = Now.AddDays(7);

    private const string Hash1 = "hash-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash2 = "hash-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Hash3 = "hash-cccccccccccccccccccccccccccccccccccc";

    private static RefreshToken CreateInitial() =>
        RefreshToken.CreateNew(
            userId: Guid.NewGuid(),
            tokenHash: Hash1,
            expiresAtUtc: Expiry,
            createdAtUtc: Now,
            createdFromIp: "127.0.0.1",
            createdByUserAgent: "test-ua");

    [Fact]
    public void CreateNew_should_start_a_new_family_and_be_active()
    {
        var t = CreateInitial();
        t.IsActive.Should().BeTrue();
        t.IsExpired.Should().BeFalse();
        t.IsRevoked.Should().BeFalse();
        t.RevokedAtUtc.Should().BeNull();
        t.ReplacedByTokenId.Should().BeNull();
        t.FamilyId.Should().NotBeEmpty();
        t.TokenHash.Should().Be(Hash1);
    }

    [Fact]
    public void Rotate_should_mark_old_token_revoked_and_replaced_by_new()
    {
        var oldToken = CreateInitial();

        // The new token is constructed with the same family id (rotation stays in family).
        var newToken = RefreshToken.CreateRotation(
            userId: oldToken.UserId,
            tokenHash: Hash2,
            familyId: oldToken.FamilyId,
            expiresAtUtc: Expiry,
            createdAtUtc: Now.AddSeconds(1),
            createdFromIp: "127.0.0.1",
            createdByUserAgent: "test-ua");

        oldToken.Rotate(newToken, Now.AddSeconds(1));

        oldToken.IsRevoked.Should().BeTrue();
        oldToken.IsActive.Should().BeFalse();
        oldToken.ReplacedByTokenId.Should().Be(newToken.Id);
        newToken.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Rotate_should_throw_if_old_token_already_revoked_reuse_signal()
    {
        var oldToken = CreateInitial();
        var newToken = RefreshToken.CreateRotation(
            userId: oldToken.UserId,
            tokenHash: Hash2,
            familyId: oldToken.FamilyId,
            expiresAtUtc: Expiry,
            createdAtUtc: Now.AddSeconds(1),
            createdFromIp: null,
            createdByUserAgent: null);

        oldToken.Rotate(newToken, Now.AddSeconds(1));

        // Attempt to rotate the SAME old token again — defense signal.
        var act = () =>
        {
            var thirdToken = RefreshToken.CreateRotation(
                userId: oldToken.UserId,
                tokenHash: Hash3,
                familyId: oldToken.FamilyId,
                expiresAtUtc: Expiry,
                createdAtUtc: Now.AddSeconds(2),
                createdFromIp: null,
                createdByUserAgent: null);
            oldToken.Rotate(thirdToken, Now.AddSeconds(2));
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot rotate a token that is already revoked or expired.");
    }

    [Fact]
    public void Rotate_should_throw_if_replacement_belongs_to_different_family()
    {
        var oldToken = CreateInitial();
        var otherFamilyToken = RefreshToken.CreateNew(
            userId: oldToken.UserId,
            tokenHash: Hash2,
            expiresAtUtc: Expiry,
            createdAtUtc: Now,
            createdFromIp: null,
            createdByUserAgent: null);

        var act = () => oldToken.Rotate(otherFamilyToken, Now.AddSeconds(1));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Replacement token must belong to the same family.");
    }

    [Fact]
    public void Revoke_should_be_idempotent()
    {
        var token = CreateInitial();
        token.Revoke("USER_LOGOUT", Now.AddSeconds(1));
        token.IsRevoked.Should().BeTrue();
        var firstRevokedAt = token.RevokedAtUtc;

        token.Revoke("REVOKE_AGAIN", Now.AddSeconds(2));
        token.RevokedAtUtc.Should().Be(firstRevokedAt);
        token.RevocationReason.Should().Be("USER_LOGOUT");
    }

    [Fact]
    public void CreateNew_should_reject_empty_user_id()
    {
        var act = () => RefreshToken.CreateNew(
            userId: Guid.Empty,
            tokenHash: Hash1,
            expiresAtUtc: Expiry,
            createdAtUtc: Now,
            createdFromIp: null,
            createdByUserAgent: null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateRotation_should_reject_empty_family_id()
    {
        var act = () => RefreshToken.CreateRotation(
            userId: Guid.NewGuid(),
            tokenHash: Hash2,
            familyId: Guid.Empty,
            expiresAtUtc: Expiry,
            createdAtUtc: Now,
            createdFromIp: null,
            createdByUserAgent: null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsExpired_should_be_true_when_past_expiry()
    {
        var token = RefreshToken.CreateNew(
            userId: Guid.NewGuid(),
            tokenHash: Hash1,
            expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
            createdAtUtc: DateTimeOffset.UtcNow.AddDays(-8),
            createdFromIp: null,
            createdByUserAgent: null);
        token.IsExpired.Should().BeTrue();
        token.IsActive.Should().BeFalse();
    }
}
