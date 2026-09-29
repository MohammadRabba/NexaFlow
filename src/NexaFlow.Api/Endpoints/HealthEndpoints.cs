using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexaFlow.Api.Endpoints;

/// <summary>
///     Health check endpoints (section 33).
///     <list type="bullet">
///         <item><c>GET /health/live</c> — process is running; no dependencies checked.</item>
///         <item><c>GET /health/ready</c> — process can serve traffic; dependencies (Postgres) checked.</item>
///         <item><c>GET /health</c> — convenience alias for <c>ready</c>.</item>
///     </list>
/// </summary>
internal static class HealthEndpoints
{
    public static IEndpointConventionBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/health").WithTags("Health").AllowAnonymous();
        group.MapGet("/live", LiveCheck);
        group.MapGet("/ready", ReadyCheckAsync);
        group.MapGet(string.Empty, ReadyCheckAsync);
        return group;
    }

    private static IResult LiveCheck() => Results.Ok(new { status = "Healthy", kind = "live" });

    private static async Task<IResult> ReadyCheckAsync(
        HealthCheckService healthChecks,
        CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        return report.Status == HealthStatus.Healthy
            ? Results.Ok(new { status = report.Status.ToString(), kind = "ready" })
            : Results.Problem(
                title: "Dependencies not ready",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: report.Status.ToString());
    }
}
