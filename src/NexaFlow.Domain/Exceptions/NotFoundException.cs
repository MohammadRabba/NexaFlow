namespace NexaFlow.Domain.Exceptions;

/// <summary>
///     Raised when an operation targets an entity that does not exist or that the
///     caller is not allowed to discover (section 27 — leak-avoidance: returning 404
///     rather than 403 prevents confirming that a resource exists in another tenant).
/// </summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object id)
        : base($"{entityName} with id '{id}' was not found.", "NOT_FOUND")
    {
        EntityName = entityName;
        Id = id;
    }

    public NotFoundException(string message) : base(message, "NOT_FOUND")
    {
        EntityName = string.Empty;
        Id = string.Empty;
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, "NOT_FOUND", innerException)
    {
        EntityName = string.Empty;
        Id = string.Empty;
    }

    public string EntityName { get; }
    public object Id { get; }
}
