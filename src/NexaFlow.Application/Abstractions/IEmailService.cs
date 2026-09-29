namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for sending emails (welcome / verification / password reset / notifications).
///     <para>
///         <b>DEFERRED</b> — not implemented in Phase 1. Phase 2 introduces a real sender
///         (SMTP or third-party API) and Phase 6 routes through the Outbox worker.
///     </para>
///     <para>
///         The contract is here so Phase 2 handlers can depend on it without restructuring.
///         No DI registration in Phase 1 — registering a fake implementation that pretends
///         to send would violate Rule: "Do not generate fake implementations just to
///         satisfy compilation."
///     </para>
/// </summary>
public interface IEmailService
{
    Task SendAsync(EmailRequest request, CancellationToken cancellationToken = default);
}

public sealed record EmailRequest(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null);
