using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     An organization-scoped label. Shared across projects within the same org.
/// </summary>
public class Label : AuditableEntity, ITenantEntity
{
    private Label() { }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Color { get; private set; }

    public static Label Create(Guid organizationId, string name, string? color, DateTimeOffset atUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 50)
            throw new ArgumentException("Label name must not exceed 50 characters.");

        return new Label
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Color = color,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    public void Rename(string newName, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.Length > 50)
            throw new ArgumentException("Label name must not exceed 50 characters.");
        if (string.Equals(Name, newName, StringComparison.Ordinal)) return;
        Name = newName.Trim();
        UpdatedAtUtc = atUtc;
    }
}
