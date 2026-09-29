using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Generates and validates JWT access tokens (section 8). Implementations
///     must use a strong signing key (HS256 minimum). The plaintext key is loaded
///     from configuration and never logged.
///     <para>
///         Access token claims (per Phase 2 design):
///         <list type="bullet">
///             <item><c>sub</c> — user id (Guid, as string).</item>
///             <item><c>email</c> — display email (informational only; never authoritative for authz).</item>
///             <item><c>email_verified</c> — boolean at issuance time; the database is the source of truth.</item>
///             <item><c>iat</c> / <c>exp</c> / <c>jti</c> — standard JWT fields.</item>
///         </list>
///     </para>
///     <para>
///         <b>NO organization membership or role claims.</b> Section 9 / user directive:
///         "Do not use JWT claims as the authoritative source for current permissions
///         or organization membership if that would allow stale authorization after
///         membership/role changes." Authorization checks hit the database on every
///         authorization-sensitive operation. Phase 7 may cache membership with a short TTL.
///     </para>
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    ///     Issue a short-lived access token (15 minutes by default). The returned string
    ///     is the signed JWT. Never log this string.
    /// </summary>
    string IssueAccessToken(User user, DateTimeOffset issuedAtUtc);

    /// <summary>
    ///     Validate the signature and expiry of an access token. Returns the user id
    ///     from the <c>sub</c> claim on success, or null on any validation failure.
    /// </summary>
    Guid? ValidateAccessToken(string? token);
}
