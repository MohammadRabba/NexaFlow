namespace NexaFlow.Domain.Exceptions;

/// <summary>
///     Raised when an operation violates a tenant boundary (e.g., attempting to
///     assign a task to a user who is not a member of the task's organization).
///     Defense-in-depth (section 6 — multiple protection layers).
/// </summary>
public class TenantViolationException : DomainException
{
    public TenantViolationException(string message)
        : base(message, "TENANT_VIOLATION")
    {
        TenantId = Guid.Empty;
        UserId = Guid.Empty;
    }

    public TenantViolationException(string message, Exception innerException)
        : base(message, "TENANT_VIOLATION", innerException)
    {
        TenantId = Guid.Empty;
        UserId = Guid.Empty;
    }

    public TenantViolationException(Guid tenantId, Guid userId, string message)
        : base(message, "TENANT_VIOLATION")
    {
        TenantId = tenantId;
        UserId = userId;
    }

    public Guid TenantId { get; }
    public Guid UserId { get; }
}
