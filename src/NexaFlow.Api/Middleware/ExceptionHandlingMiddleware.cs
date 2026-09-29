using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Api.Middleware;

/// <summary>
///     Centralized exception handler. Translates CLR exceptions into RFC 7807
///     Problem Details responses (section 31). Never exposes stack traces or
///     internal implementation details in production (section 40).
///     <para>
///         Mapping:
///         <list type="table">
///             <list><header><see cref="DomainException"/></header></list>
///             <list><header><see cref="NotFoundException"/></header></list>  → 404
///             <list><header><see cref="TenantViolationException"/></header></list>  → 403
///             <list><header><see cref="InvalidStateTransitionException"/></header></list>  → 409
///             <list><header><see cref="ValidationException"/></header></list>  → 422
///         </list>
///     </para>
/// </summary>
internal sealed class ExceptionHandlingMiddleware
{
    private const string ProblemDetailsContentType = "application/problem+json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // We always serialize problem details with a consistent, web-friendly contract.
        WriteIndented = false
    };

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, Exception ex)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var (status, title, errorCode, detail) = MapException(ex);

        // 5xx => log as Error; 4xx client errors => log as Information (no server fault)
        if (status >= 500)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path} (trace={TraceId})",
                context.Request.Method, context.Request.Path, traceId);
        }
        else
        {
            _logger.LogInformation("Rejecting {Method} {Path} with {Status} ({ErrorCode}): {Detail} (trace={TraceId})",
                context.Request.Method, context.Request.Path, status, errorCode, detail, traceId);
        }

        var problem = new ProblemDetailsPayload
        {
            Type = $"https://api.nexaflow.com/errors/{errorCode.ToLowerInvariant()}",
            Title = title,
            Status = status,
            Detail = _env.IsDevelopment() ? ex.ToString() : detail,
            ErrorCode = errorCode,
            TraceId = traceId,
            Errors = ex is ValidationException ve ? MapValidationErrors(ve) : null
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = ProblemDetailsContentType;
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions);
    }

    private static (int status, string title, string errorCode, string detail) MapException(Exception ex)
    {
        return ex switch
        {
            // Refresh token reuse detection — security-sensitive; never reveal why to the client.
            // Maps to 401 INVALID_REFRESH_TOKEN (section 8 — do not reveal account state).
            Application.Abstractions.RefreshTokenReuseException => (
                StatusCodes.Status401Unauthorized,
                "Invalid or expired refresh token",
                "INVALID_REFRESH_TOKEN",
                "Invalid or expired refresh token."),
            ValidationException ve => (
                StatusCodes.Status422UnprocessableEntity,
                "Validation failed",
                "VALIDATION",
                "One or more inputs failed validation."),
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                "NOT_FOUND",
                ex.Message),
            TenantViolationException => (
                StatusCodes.Status403Forbidden,
                "Access denied",
                "TENANT_VIOLATION",
                "You are not allowed to access this resource."),
            InvalidStateTransitionException => (
                StatusCodes.Status409Conflict,
                "Invalid state transition",
                "INVALID_STATE_TRANSITION",
                ex.Message),
            DomainException de => (
                StatusCodes.Status400BadRequest,
                "Domain rule violated",
                de.ErrorCode,
                ex.Message),
            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "Request cancelled",
                "REQUEST_CANCELLED",
                "The client cancelled the request."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "INTERNAL",
                "An unexpected error occurred.")
        };
    }

    private static Dictionary<string, string[]>? MapValidationErrors(ValidationException ex)
    {
        return ex.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private sealed class ProblemDetailsPayload
    {
        public string Type { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public int Status { get; init; }
        public string? Detail { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public string? TraceId { get; init; }
        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
