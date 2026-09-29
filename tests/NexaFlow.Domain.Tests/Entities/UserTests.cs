using FluentAssertions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Tests for <see cref="User" /> aggregate — section 8 (Authentication) and
///     section 17 (Domain Business Rules). The aggregate enforces:
///     <list type="bullet">
///         <item>Email verification idempotency.</item>
///         <item>Account lockout after N failed attempts.</item>
///         <item>Successful login resets the failure counter.</item>
///         <item>Manual unlock clears lockout state.</item>
///     </list>
/// </summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset AtUtc =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static User CreateUser() =>
        User.Create(
            email: NexaFlow.Domain.ValueObjects.Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: "$2a$12$placeholderhashxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            emailVerificationTokenHash: "verification-hash-placeholder",
            emailVerificationTokenExpiresAtUtc: AtUtc.AddDays(1),
            createdAtUtc: AtUtc);

    [Fact]
    public void Create_should_initialize_pending_verification_state()
    {
        var user = CreateUser();
        user.EmailVerified.Should().BeFalse();
        user.EmailVerifiedAtUtc.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
        user.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void VerifyEmail_should_be_idempotent()
    {
        var user = CreateUser();

        user.VerifyEmail(AtUtc);
        var firstVerifiedAt = user.EmailVerifiedAtUtc;
        firstVerifiedAt.Should().Be(AtUtc);
        user.EmailVerified.Should().BeTrue();

        // Second call must not change the timestamp
        user.VerifyEmail(AtUtc.AddHours(1));
        user.EmailVerifiedAtUtc.Should().Be(firstVerifiedAt);
    }

    [Fact]
    public void RegisterFailedLogin_should_lock_after_threshold()
    {
        var user = CreateUser();
        const int maxAttempts = 3;
        var lockoutDuration = TimeSpan.FromMinutes(15);
        var now = DateTimeOffset.UtcNow;

        // First two failures do not lock the account
        user.RegisterFailedLogin(maxAttempts, lockoutDuration, now);
        user.RegisterFailedLogin(maxAttempts, lockoutDuration, now.AddSeconds(1));
        user.IsLockedOut.Should().BeFalse();

        // Third failure hits the threshold
        user.RegisterFailedLogin(maxAttempts, lockoutDuration, now.AddSeconds(2));
        user.IsLockedOut.Should().BeTrue();
        user.LockoutEndUtc.Should().Be(now.AddSeconds(2).Add(lockoutDuration));
    }

    [Fact]
    public void RegisterSuccessfulLogin_should_reset_failure_counter()
    {
        var user = CreateUser();
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), AtUtc);
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), AtUtc.AddSeconds(1));

        user.RegisterSuccessfulLogin(AtUtc.AddSeconds(2));

        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
        user.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void Unlock_should_clear_lockout_state()
    {
        var user = CreateUser();
        var now = DateTimeOffset.UtcNow;
        // Force lockout
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), now.AddSeconds(i));
        }
        user.IsLockedOut.Should().BeTrue();

        user.Unlock(now.AddMinutes(1));

        user.IsLockedOut.Should().BeFalse();
        user.LockoutEndUtc.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public void ChangePassword_should_reset_failure_counter()
    {
        var user = CreateUser();
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), AtUtc);
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15), AtUtc.AddSeconds(1));

        user.ChangePassword("$2a$12$newhashxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", AtUtc.AddSeconds(2));

        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
    }

    [Fact]
    public void OrganizationMember_ChangeRole_should_block_owner_role_changes()
    {
        // Arrange — an Owner's role cannot be changed directly; ownership must be transferred
        var owner = OrganizationMember.CreateAsOwner(Guid.NewGuid(), Guid.NewGuid(), AtUtc);

        // Act
        var act = () => owner.ChangeRole(OrganizationRole.Admin, Guid.NewGuid(), AtUtc.AddSeconds(1));

        // Assert — explicit guard
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot change an Owner's role*");
    }
}
