using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Behaviors;

/// <summary>
///     Cross-cutting pipeline behavior that logs every MediatR request with a
///     correlation scope, request name, and elapsed time (section 32 — structured logging,
///     section 39 — performance: alert on slow handlers).
///     <para>
///         Secrets are NEVER logged (section 32 — never log passwords, tokens, reset tokens).
///         This behavior only logs the request type name and the time, never the request payload.
///     </para>
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService? _currentUser;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        ICurrentUserService? currentUser = null)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUser?.UserId;
        var traceId = _currentUser?.TraceId;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["MediatRRequest"] = requestName,
            ["UserId"] = userId ?? Guid.Empty,
            ["TraceId"] = traceId ?? string.Empty
        }))
        {
            _logger.LogInformation("Handling {Request}", requestName);
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await next(cancellationToken);
                sw.Stop();
                _logger.LogInformation(
                    "Handled {Request} in {ElapsedMs}ms",
                    requestName,
                    sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Failed {Request} after {ElapsedMs}ms",
                    requestName,
                    sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
