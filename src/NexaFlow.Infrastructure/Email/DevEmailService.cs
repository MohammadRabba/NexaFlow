using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Infrastructure.Email;

/// <summary>
///     <b>Development-only</b> email service. Writes the email as JSON to console + an
///     append-only file under <c>%TEMP%/nexaflow-dev-emails/</c> so that local developers
///     can see what would have been sent. Marked clearly as a development implementation.
/// </summary>
/// <remarks>
///     <para>
///         Per Phase 2 directive: "email delivery can use a clearly documented development
///         implementation if a real provider is not yet configured, but do not fake
///         successful production email delivery."
///     </para>
///     <para>
///         This implementation is registered ONLY when <c>IHostEnvironment.IsDevelopment()</c>
///         is true. In Production, the API host registers <see cref="NotConfiguredEmailService" />
///         which throws <see cref="InvalidOperationException" /> on every send — production
///         callers MUST wire a real SMTP or external API sender before shipping.
///     </para>
///     <para>
///         NEVER log the plaintext email body if it contains a verification / reset token —
///         the file is written to a local path that may be shared. We redact the body to
///         a placeholder before logging; the file (for dev only) keeps the full body so the
///         developer can see the token to test the flow locally.
///     </para>
/// </remarks>
public sealed class DevEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<DevEmailService> _logger;
    private readonly string _emailsDirectory;

    public DevEmailService(ILogger<DevEmailService> logger)
    {
        _logger = logger;
        _emailsDirectory = Path.Combine(Path.GetTempPath(), "nexaflow-dev-emails");
        Directory.CreateDirectory(_emailsDirectory);
    }

    public async Task SendAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var fileName = $"{stamp}_{request.To.Replace('@', '_')}.json";
        var fullPath = Path.Combine(_emailsDirectory, fileName);

        // The file keeps the full body so developers can copy the verification / reset
        // token out and use it in their test client.
        var record = new
        {
            Stamp = stamp,
            request.To,
            request.Subject,
            request.HtmlBody,
            request.TextBody,
            Service = "DevEmailService (DEVELOPMENT ONLY — never use in production)"
        };
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);

        // The structured log only includes the recipient + subject + the file path —
        // NEVER the body (which may contain the plaintext verification / reset token).
        _logger.LogInformation(
            "DEV email sent to {Recipient} subject=\"{Subject}\" saved to {FilePath}. " +
            "Open the file to view the body / token (not logged here).",
            Redact(request.To),
            request.Subject,
            fullPath);
    }

    /// <summary>
    ///     Redact the local part of an email for logging. <c>alice@example.com</c> → <c>a***@example.com</c>.
    ///     Sufficient for correlation; insufficient to expose the actual address.
    /// </summary>
    private static string Redact(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "<redacted>";
        var localPart = email[..atIndex];
        var domain = email[atIndex..];
        var maskedLocal = localPart.Length <= 1
            ? "*"
            : $"{localPart[0]}{new string('*', Math.Max(1, localPart.Length - 1))}";
        return $"{maskedLocal}{domain}";
    }
}

/// <summary>
///     Registered when no real email service is configured. Throws on every send —
///     production callers MUST configure a real sender (SMTP / external API).
/// </summary>
public sealed class NotConfiguredEmailService : IEmailService
{
    private const string Message =
        "IEmailService is not configured for this environment. Phase 2 ships with a " +
        "DevEmailService for Development; Production must register a real email sender " +
        "(SMTP / external API) via Infrastructure DI. Do not ship this implementation.";

    public Task SendAsync(EmailRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);
}
