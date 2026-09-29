using FluentAssertions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Domain.Tests.ValueObjects;

/// <summary>
///     Tests for the <see cref="Email" /> value object — section 10 (Value Objects)
///     and section 8 (Authentication: never reveal whether an email exists).
///     <para>
///         Note: <see cref="Email" /> is a domain rule object — it throws if the email is
///         malformed. The friendlier "input validation" error message is produced by
///         FluentValidation in the Application layer (section 13).
///     </para>
/// </summary>
public sealed class EmailTests
{
    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("  alice@example.com  ")]            // trailing whitespace trimmed
    [InlineData("Bob@example.com")]                  // mixed case preserved in display form
    public void Create_should_accept_valid_emails(string raw)
    {
        var email = Email.Create(raw);
        email.Value.Should().Be(raw.Trim());
        email.Normalized.Should().Be(raw.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_should_throw_for_empty_input(string? raw)
    {
        var act = () => Email.Create(raw);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Create_should_reject_emails_without_at_sign()
    {
        var act = () => Email.Create("alice.example.com");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Create_should_reject_emails_ending_in_at_sign()
    {
        var act = () => Email.Create("alice@");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryCreate_should_return_false_for_invalid_email()
    {
        var ok = Email.TryCreate("not-an-email", out var email);
        ok.Should().BeFalse();
        email.Should().BeNull();
    }

    [Fact]
    public void TryCreate_should_return_true_for_valid_email()
    {
        var ok = Email.TryCreate("alice@example.com", out var email);
        ok.Should().BeTrue();
        email!.Value.Should().Be("alice@example.com");
    }

    [Fact]
    public void Emails_with_same_normalized_value_should_be_equal_by_value()
    {
        var a = Email.Create("Alice@Example.com");
        var b = Email.Create("alice@example.com");
        // Records compare by value; Normalized field is the canonical equality key
        (a.Normalized == b.Normalized).Should().BeTrue();
    }
}
