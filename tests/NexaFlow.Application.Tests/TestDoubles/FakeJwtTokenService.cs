using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     Test double for <see cref="IJwtTokenService" />. Issues fake "JWT-shaped" strings
///     that the test asserts on. For real JWT validation tests, see the IntegrationTests
///     project — those use the real JwtTokenService.
/// </summary>
public sealed class FakeJwtTokenService : IJwtTokenService
{
    public string IssuedAccessToken { get; private set; } = "fake-jwt-token";
    public Guid? LastUserId { get; private set; }

    public string IssueAccessToken(User user, DateTimeOffset issuedAtUtc)
    {
        LastUserId = user.Id;
        IssuedAccessToken = $"fake-jwt.{user.Id}.{issuedAtUtc.Ticks}";
        return IssuedAccessToken;
    }

    public Guid? ValidateAccessToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        return Guid.TryParse(parts[1], out var userId) ? userId : null;
    }
}
