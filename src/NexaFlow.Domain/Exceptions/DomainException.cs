namespace NexaFlow.Domain.Exceptions;

/// <summary>
///     Base class for all domain-level exceptions. Thrown when a business invariant
///     is violated (section 17). Application layer translates these into HTTP 4xx
///     responses with RFC 7807 Problem Details (section 31).
/// </summary>
public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string message, string errorCode, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? "DOMAIN_ERROR"
            : errorCode;
    }

    // Standard constructors (CA1032) — kept for interop with .NET exception pipelines
    public DomainException() : this("A domain invariant was violated.", "DOMAIN_ERROR") { }

    public DomainException(string message) : this(message, "DOMAIN_ERROR") { }

    public DomainException(string message, Exception innerException)
        : this(message, "DOMAIN_ERROR", innerException) { }
}
