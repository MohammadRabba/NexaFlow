using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Authentication;

/// <summary>
///     JWT access-token service. Uses HS256 with a 256-bit signing key loaded from
///     configuration. Per Phase 2 design (see docs/authentication.md):
///     <list type="bullet">
///         <item><b>NO</b> organization membership or role claims in the JWT —
///             authorization checks hit the database on every authorization-sensitive
///             operation. The database is the authoritative source; JWT is only a
///             short-lived "you are this user" assertion.</item>
///         <item>Claims: <c>sub</c> (user id), <c>email</c> (display), <c>email_verified</c>
///             (at issuance), <c>jti</c> (unique token id), <c>iat</c> + <c>exp</c>.</item>
///         <item>Plaintext signing key is NEVER logged. Validation failures log a
///             sanitized message.</item>
///     </list>
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly AuthOptions _options;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<AuthOptions> options, ILogger<JwtTokenService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string IssueAccessToken(User user, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Id == Guid.Empty)
            throw new ArgumentException("User id must not be empty.", nameof(user));

        var expiresAtUtc = issuedAtUtc + _options.AccessTokenLifetime;

        // NEVER include organization membership or role claims in the JWT.
        // Authorization is database-validated on every authorization-sensitive operation.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email.Value),
            new Claim("email_verified", user.EmailVerified.ToString().ToLowerInvariant()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                issuedAtUtc.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var key = GetSigningKey();
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.JwtIssuer,
            Audience = _options.JwtAudience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = issuedAtUtc.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            IssuedAt = issuedAtUtc.UtcDateTime,
            SigningCredentials = signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    public Guid? ValidateAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var key = GetSigningKey();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _options.JwtIssuer,
                ValidAudience = _options.JwtAudience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1),
                RequireExpirationTime = true,
                ValidateLifetime = true
            };

            var principal = _tokenHandler.ValidateToken(
                token,
                validationParameters,
                out var securityToken);

            if (securityToken is not JwtSecurityToken jwt)
            {
                _logger.LogInformation("Access token rejected: not a JWT.");
                return null;
            }

            var sub = jwt.Subject;
            if (!Guid.TryParse(sub, out var userId))
            {
                _logger.LogInformation("Access token rejected: 'sub' claim is not a valid Guid.");
                return null;
            }

            return userId;
        }
        catch (Exception ex) when (ex is SecurityTokenExpiredException
                                    or SecurityTokenInvalidSignatureException
                                    or SecurityTokenInvalidLifetimeException
                                    or SecurityTokenInvalidIssuerException
                                    or SecurityTokenInvalidAudienceException
                                    or SecurityTokenException
                                    or ArgumentException)
        {
            // Never log the token itself. Log only the exception type and message.
            _logger.LogInformation("Access token validation failed: {ExceptionType}: {Message}",
                ex.GetType().Name,
                ex.Message);
            return null;
        }
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.JwtSigningKey);
        if (keyBytes.Length < 32)
        {
            // Options validation should have caught this at startup, but be defensive.
            throw new InvalidOperationException(
                "JWT signing key is too short. Must be at least 256 bits (32 characters).");
        }
        return new SymmetricSecurityKey(keyBytes);
    }
}
