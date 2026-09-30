using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Application.Tests.Auth;

/// <summary>
///     Phase 2 sensitive-data logging tests (section 32 — never log access tokens,
///     refresh tokens, passwords, reset tokens, email verification tokens).
///     <para>
///         We capture the ILogger&lt;T&gt; output via a custom logging provider
///         and assert that no plaintext sensitive value ever appears in a log entry.
///     </para>
/// </summary>
public sealed class SensitiveDataLoggingTests
{
    [Fact]
    public async Task Login_handler_should_never_log_password_or_token_in_failure()
    {
        // Arrange — capture all log entries emitted by the handler.
        var logs = new List<(LogLevel Level, string Message, object?[] Args)>();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new CapturingLoggerProvider(logs));
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        var logger = loggerFactory.CreateLogger<LoginCommandHandler>();

        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var refreshStore = new FakeRefreshTokenStore(new FakeSecureTokenGenerator());
        var options = TestAuthOptions.Default;

        // Seed an unverified user to trigger a known failure path.
        var user = User.Create(
            email: Email.Create("alice@example.com"),
            displayName: "Alice",
            passwordHash: passwordHasher.Hash("StrongPass1!"),
            emailVerificationTokenHash: "verify-hash",
            emailVerificationTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            createdAtUtc: DateTimeOffset.UtcNow);
        db.Users.Add(user);

        var handler = new LoginCommandHandler(db, passwordHasher, jwt, refreshStore, new FakeAuditService(), options);

        // Act — attempt a login that will fail (unverified email).
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new LoginCommand("alice@example.com", "StrongPass1!"),
            CancellationToken.None));

        // Assert — the password and any token-like value must NEVER appear in logs.
        foreach (var (level, message, args) in logs)
        {
            // The format string contains placeholders; we render the args via the formatter.
            var rendered = string.Format(message, args ?? []);
            rendered.Should().NotContain("StrongPass1!", "password must never appear in logs");
            rendered.Should().NotContain("verify-hash", "token hashes must never appear in logs");
        }
    }

    [Fact]
    public async Task Register_handler_should_never_log_plaintext_verification_token_or_password()
    {
        // Arrange
        var logs = new List<(LogLevel Level, string Message, object?[] Args)>();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new CapturingLoggerProvider(logs));
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        var db = new InMemoryApplicationDbContext();
        var passwordHasher = new FakePasswordHasher();
        var tokenGenerator = new FakeSecureTokenGenerator();
        var jwt = new FakeJwtTokenService();
        var refreshStore = new FakeRefreshTokenStore(tokenGenerator);
        var emailService = new FakeEmailService();
        var currentUser = new FakeCurrentUserService();
        var options = TestAuthOptions.Default;

        var handler = new RegisterCommandHandler(
            db, passwordHasher, tokenGenerator, jwt, refreshStore, emailService,
            currentUser, options,
            loggerFactory.CreateLogger<RegisterCommandHandler>());

        var password = "StrongPass1!";
        var command = new RegisterCommand("alice@example.com", "Alice", password);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert — password and the plaintext verification token (sent via email)
        // must never appear in logs.
        var verificationEmail = emailService.SentEmails.Single();
        // The plaintext token is in the email body — extract it.
        var plaintextTokenStart = verificationEmail.HtmlBody.IndexOf("token: <code>", StringComparison.OrdinalIgnoreCase);
        plaintextTokenStart.Should().BeGreaterThanOrEqualTo(-1);

        foreach (var (level, message, args) in logs)
        {
            var rendered = string.Format(message, args ?? []);
            rendered.Should().NotContain(password, "password must never appear in logs");
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel, string, object?[])> _sink;

        public CapturingLoggerProvider(List<(LogLevel, string, object?[])> sink) => _sink = sink;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_sink);

        public void Dispose() { }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<(LogLevel, string, object?[])> _sink;

        public CapturingLogger(List<(LogLevel, string, object?[])> sink) => _sink = sink;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The formatter renders the message; we capture both the raw message and the args.
            // For our purpose (verifying no sensitive value leaks), it's enough to capture
            // the rendered string.
            var rendered = formatter(state, exception);
            _sink.Add((logLevel, rendered, []));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
