using FluentAssertions;
using NexaFlow.Domain.Entities;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Tests for the Phase 2 additions to the <see cref="User" /> aggregate:
///     email verification + password reset token issue/consume, single-use semantics.
/// </summary>
public sealed class UserTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private const string PasswordHash = "$2a$12$placeholderhashxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    private const string TokenHash1 = "hash-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenHash2 = "hash-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static User CreateUser() =>
        User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: PasswordHash,
            emailVerificationTokenHash: TokenHash1,
            emailVerificationTokenExpiresAtUtc: Now.AddHours(24),
            createdAtUtc: Now);

    [Fact]
    public void Create_with_email_verification_token_should_initialize_unverified_state()
    {
        var user = CreateUser();
        user.EmailVerified.Should().BeFalse();
        user.EmailVerificationTokenHash.Should().Be(TokenHash1);
        user.EmailVerificationTokenExpiresAtUtc.Should().NotBeNull();
        user.PasswordChangedAtUtc.Should().BeNull();
    }

    [Fact]
    public void ConsumeEmailVerificationToken_with_matching_hash_should_verify_email_and_clear_token()
    {
        var user = CreateUser();

        var consumed = user.ConsumeEmailVerificationToken(TokenHash1, Now.AddHours(1));

        consumed.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();
        user.EmailVerifiedAtUtc.Should().Be(Now.AddHours(1));
        user.EmailVerificationTokenHash.Should().BeNull();
        user.EmailVerificationTokenExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void ConsumeEmailVerificationToken_with_mismatched_hash_should_return_false()
    {
        var user = CreateUser();

        var consumed = user.ConsumeEmailVerificationToken(TokenHash2, Now.AddHours(1));

        consumed.Should().BeFalse();
        user.EmailVerified.Should().BeFalse();
        user.EmailVerificationTokenHash.Should().Be(TokenHash1);
    }

    [Fact]
    public void ConsumeEmailVerificationToken_after_expiry_should_return_false()
    {
        var user = CreateUser();

        var consumed = user.ConsumeEmailVerificationToken(TokenHash1, Now.AddHours(25));

        consumed.Should().BeFalse();
        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void ConsumeEmailVerificationToken_when_already_verified_should_be_idempotent()
    {
        var user = CreateUser();
        user.ConsumeEmailVerificationToken(TokenHash1, Now.AddHours(1));

        // Second consumption with the SAME hash should return true (idempotent),
        // but the token hash is already null.
        var consumed = user.ConsumeEmailVerificationToken(TokenHash1, Now.AddHours(2));
        consumed.Should().BeTrue();
    }

    [Fact]
    public void IssueEmailVerificationToken_should_overwrite_previous_token()
    {
        var user = CreateUser();

        user.IssueEmailVerificationToken(TokenHash2, Now.AddHours(48), Now);

        user.EmailVerificationTokenHash.Should().Be(TokenHash2);
        user.EmailVerificationTokenExpiresAtUtc.Should().Be(Now.AddHours(48));
    }

    [Fact]
    public void ConsumePasswordResetToken_should_set_new_password_and_clear_token()
    {
        var user = CreateUser();
        user.IssuePasswordResetToken(TokenHash1, Now.AddHours(1), Now);

        var consumed = user.ConsumePasswordResetToken(TokenHash1, "new-hash-value", Now.AddMinutes(30));

        consumed.Should().BeTrue();
        user.PasswordHash.Should().Be("new-hash-value");
        user.PasswordChangedAtUtc.Should().Be(Now.AddMinutes(30));
        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordResetTokenExpiresAtUtc.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
    }

    [Fact]
    public void ConsumePasswordResetToken_after_expiry_should_return_false()
    {
        var user = CreateUser();
        user.IssuePasswordResetToken(TokenHash1, Now.AddHours(1), Now);

        var consumed = user.ConsumePasswordResetToken(TokenHash1, "new-hash", Now.AddHours(2));

        consumed.Should().BeFalse();
        user.PasswordHash.Should().Be(PasswordHash);
        user.PasswordResetTokenHash.Should().Be(TokenHash1);
    }

    [Fact]
    public void ChangePassword_should_stamp_PasswordChangedAtUtc_and_reset_failure_counter()
    {
        var user = CreateUser();
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), Now);
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), Now.AddSeconds(1));

        user.ChangePassword("new-hash", Now.AddSeconds(2));

        user.PasswordHash.Should().Be("new-hash");
        user.PasswordChangedAtUtc.Should().Be(Now.AddSeconds(2));
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
    }
}
