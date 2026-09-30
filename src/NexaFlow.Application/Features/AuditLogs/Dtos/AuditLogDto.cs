using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Features.AuditLogs.Dtos;

/// <summary>
///     Read model for an <see cref="AuditLog" /> row. The
///     <see cref="OldValues" /> / <see cref="NewValues" /> fields are JSON strings
///     (spec §25: "Use structured JSON for old/new values where appropriate") —
///     the API layer may parse them client-side. They are returned as strings to
///     preserve the original payload (the JSON shape is whatever the caller of
///     <see cref="IAuditService" /> chose to serialize).
/// </summary>
public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    Guid? OrganizationId,
    string Action,
    string Entity,
    Guid? EntityId,
    string? OldValues,
    string? NewValues,
    string? IPAddress,
    DateTimeOffset Timestamp);
