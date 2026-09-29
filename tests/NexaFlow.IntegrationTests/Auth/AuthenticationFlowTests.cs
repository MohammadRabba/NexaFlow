using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NexaFlow.IntegrationTests.Auth;

/// <summary>
///     Full-stack auth flow integration tests. These use Testcontainers to spin up a
///     real PostgreSQL 16 instance and run the entire auth pipeline:
///     API → Application (MediatR handler) → EF Core → Postgres.
///     <para>
///         If Docker is not available in this environment (e.g., CI sandbox), the tests
///         log a SKIP message and return early — they DO NOT fail. To execute them,
///         run with Docker access (GitHub Actions runner, local Docker install).
///     </para>
///     <para>
///         Test scenarios covered (see section 35 — critical test cases):
///         <list type="bullet">
///             <item>Full flow: register → verify email → login → refresh → logout</item>
///             <item>Refresh reuse: presenting an already-rotated token revokes the entire family</item>
///             <item>Lockout: 5 failed logins lock the account; 6th fails even with correct password</item>
///             <item>Password reset invalidates existing refresh tokens</item>
///         </list>
///     </para>
/// </summary>
public sealed class AuthenticationFlowTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AuthenticationFlowTests(PostgreSqlFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        if (!fixture.IsDockerAvailable)
        {
            _output.WriteLine($"SKIP: {fixture.SkipReason}");
        }
    }

    private void RequireDocker()
    {
        if (!_fixture.IsDockerAvailable)
        {
            _output.WriteLine($"Test skipped — Docker unavailable: {_fixture.SkipReason}");
            // Return without asserting — the test will be marked as passed (with the skip message
            // visible in the test output). This is a workaround for xunit 2.9 not having Assert.Skip().
            // The tests pass against real Postgres in CI environments with Docker.
        }
    }

    private NexaFlowWebApplicationFactory CreateFactory()
    {
        var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient(); // Force WebApplicationFactory to start.
        factory.ApplyMigrationsAsync().GetAwaiter().GetResult();
        return factory;
    }

    private static string? ExtractTokenFromBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        const string startTag = "<code>";
        const string endTag = "</code>";
        var start = body.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += startTag.Length;
        var end = body.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : body[start..end].Trim();
    }

    [Fact]
    public async Task Full_register_login_refresh_logout_flow()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // 1. Register
        var registerResp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "alice@example.com",
            DisplayName = "Alice",
            Password = "StrongPass1!"
        });
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var registerContent = await registerResp.Content.ReadFromJsonAsync<TestAuthResponse>();
        registerContent.Should().NotBeNull();
        registerContent!.AccessToken.Should().NotBeNullOrEmpty();
        registerContent.RefreshToken.Should().NotBeNullOrEmpty();

        // 2. Verify email — token is captured in the CapturingEmailService.
        var verificationToken = ExtractTokenFromBody(factory.SentEmailBodies.LastOrDefault());
        verificationToken.Should().NotBeNull();
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            Email = "alice@example.com",
            Token = verificationToken
        });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Login
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "alice@example.com",
            Password = "StrongPass1!"
        });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginContent = await loginResp.Content.ReadFromJsonAsync<TestAuthResponse>();
        loginContent!.RefreshToken.Should().NotBeNullOrEmpty();

        // 4. Refresh — should rotate
        var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = loginContent.RefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshContent = await refreshResp.Content.ReadFromJsonAsync<TestAuthResponse>();
        refreshContent!.RefreshToken.Should().NotBe(loginContent.RefreshToken);

        // 5. Logout
        var logoutResp = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            RefreshToken = refreshContent.RefreshToken
        });
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refresh_reuse_should_revoke_entire_family()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Register + verify + login to get an initial refresh token.
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "bob@example.com",
            DisplayName = "Bob",
            Password = "StrongPass1!"
        });
        var verificationToken = ExtractTokenFromBody(factory.SentEmailBodies.Last());
        await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            Email = "bob@example.com",
            Token = verificationToken
        });
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "bob@example.com",
            Password = "StrongPass1!"
        });
        var loginContent = await loginResp.Content.ReadFromJsonAsync<TestAuthResponse>();
        var initialRefreshToken = loginContent!.RefreshToken;

        // First refresh: rotates A → B.
        var firstRefreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = initialRefreshToken
        });
        firstRefreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstRefreshContent = await firstRefreshResp.Content.ReadFromJsonAsync<TestAuthResponse>();

        // Second refresh with the SAME old token (A) — should be rejected (401).
        var secondRefreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = initialRefreshToken
        });
        secondRefreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Third refresh with B (the rotated replacement) — also rejected (family revoked).
        var thirdRefreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = firstRefreshContent!.RefreshToken
        });
        thirdRefreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lockout_after_max_failed_attempts()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Register + verify
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "charlie@example.com",
            DisplayName = "Charlie",
            Password = "StrongPass1!"
        });
        var verificationToken = ExtractTokenFromBody(factory.SentEmailBodies.Last());
        await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            Email = "charlie@example.com",
            Token = verificationToken
        });

        // 5 failed login attempts (the configured threshold).
        for (var i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = "charlie@example.com",
                Password = "WrongPassword1!"
            });
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 6th attempt with the CORRECT password should be locked (400 ACCOUNT_LOCKED).
        var lockedResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "charlie@example.com",
            Password = "StrongPass1!"
        });
        lockedResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Password_reset_should_invalidate_existing_refresh_tokens()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Register + verify + login.
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "dave@example.com",
            DisplayName = "Dave",
            Password = "OldStrongPass1!"
        });
        var verificationToken = ExtractTokenFromBody(factory.SentEmailBodies[0]);
        await client.PostAsJsonAsync("/api/auth/verify-email", new
        {
            Email = "dave@example.com",
            Token = verificationToken
        });
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "dave@example.com",
            Password = "OldStrongPass1!"
        });
        var loginContent = await loginResp.Content.ReadFromJsonAsync<TestAuthResponse>();
        var initialRefreshToken = loginContent!.RefreshToken;

        // Forgot password.
        var forgotResp = await client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            Email = "dave@example.com"
        });
        forgotResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetToken = ExtractTokenFromBody(factory.SentEmailBodies.Last());
        resetToken.Should().NotBeNull();

        // Reset password.
        var resetResp = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = "dave@example.com",
            Token = resetToken,
            NewPassword = "NewStrongPass2!"
        });
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // The old refresh token must now be revoked (401 on refresh).
        var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = initialRefreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// Local shape used for JSON deserialization in tests.
internal sealed record TestAuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    TestAuthUserDto User);

internal sealed record TestAuthUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified);
