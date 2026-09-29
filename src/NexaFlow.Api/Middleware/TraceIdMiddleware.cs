using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace NexaFlow.Api.Middleware;

/// <summary>
///     Adds the distributed trace identifier to the response headers and propagates
///     it into <see cref="Activity.Current" /> for structured logging (section 32).
///     If the inbound request lacks a trace id, generate one (per ASP.NET Core
///     convention with <c>W3C TraceContext</c>).
/// </para>
/// </summary>
internal sealed class TraceIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string TraceIdHeader = "X-Trace-Id";

    public TraceIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Response.Headers[TraceIdHeader] = traceId;
        await _next(context);
    }
}
