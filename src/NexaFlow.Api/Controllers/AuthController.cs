using MediatR;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Features.Auth.Commands;
using NexaFlow.Application.Features.Auth.Dtos;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Authentication endpoints (section 8). Returns RFC 7807 Problem Details on
///     domain failures (mapped by the ExceptionHandlingMiddleware). On success,
///     returns the access token (15 min), the refresh token (7 days, single-use),
///     the expiry timestamps, and the user's basic identity.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    ///     Register a new user. Issues an initial access + refresh token and an
    ///     email verification token (emailed via IEmailService in Development).
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AuthResponse>> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterCommand(
            Email: request.Email,
            DisplayName: request.DisplayName,
            Password: request.Password);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Authenticate with email + password. Issues a new access + refresh token
    ///     (start of a new family). Failed-login counters are incremented; lockout
    ///     applies after the configured threshold.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<ActionResult<AuthResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand(
            Email: request.Email,
            Password: request.Password,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Rotate a refresh token. Present the old refresh token; receive a new access
    ///     token and a new (rotated) refresh token. The old token is revoked; presenting
    ///     it again triggers family-wide revocation (reuse detection).
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> RefreshAsync(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RefreshCommand(
            RefreshToken: request.RefreshToken,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Logout: revoke the presented refresh token. Idempotent — returns 204
    ///     whether or not the token was valid (do not reveal whether it existed).
    ///     Access tokens remain valid until their natural expiry (~15 min).
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new LogoutCommand(RefreshToken: request.RefreshToken);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    ///     Initiate the password reset flow. Always returns 200 — never reveals whether
    ///     an email exists in the system. If the email is registered, a single-use reset
    ///     token is generated (hashed) and emailed (Dev: written to file).
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(Email: request.Email);
        await _mediator.Send(command, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Complete the password reset flow. The token must be valid (unexpired, unused).
    ///     On success, ALL refresh tokens for the user are revoked (defense against stolen
    ///     sessions after a password reset).
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(
            Email: request.Email,
            Token: request.Token,
            NewPassword: request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Verify the user's email using the one-time token issued at registration
    ///     or via the forgot-password endpoint (re-issues verification tokens — Phase 2+).
    ///     Single-use.
    /// </summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmailAsync(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var command = new VerifyEmailCommand(Email: request.Email, Token: request.Token);
        await _mediator.Send(command, cancellationToken);
        return Ok();
    }

    /// <summary>
    ///     Change the password for the currently-authenticated user. Requires the
    ///     old password for verification. On success, all refresh tokens are revoked
    ///     (the user must re-authenticate on other devices).
    /// </summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        // Read the user id from the authenticated principal — the JWT bearer middleware
        // populates the ClaimsPrincipal with the sub claim.
        var userIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new ChangePasswordCommand(
            UserId: userId,
            CurrentPassword: request.CurrentPassword,
            NewPassword: request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return Ok();
    }
}

// --- Request DTOs (kept in the API layer — they map 1:1 to commands) ---

public sealed record RegisterRequest(string Email, string DisplayName, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record VerifyEmailRequest(string Email, string Token);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
