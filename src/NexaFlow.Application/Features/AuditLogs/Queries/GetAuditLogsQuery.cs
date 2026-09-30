using MediatR;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.AuditLogs.Dtos;

namespace NexaFlow.Application.Features.AuditLogs.Queries;

/// <summary>
///     Page the audit log for the resolved tenant (spec §25, §29). Filters:
///     action, acting-user id, entity type, entity id, time window. Requires
///     <see cref="Authorization.Permissions.AuditLogRead" />.
/// </summary>
/// <remarks>
///     <para>
///         The query operates in two scopes:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Scope = "organization"</c> (default) — only audit rows whose
///             <c>OrganizationId</c> equals the resolved tenant. Auth events
///             (Login, etc.) that have a null <c>OrganizationId</c> are NOT included
///             here — they belong to no tenant.
///         </item>
///         <item>
///             <c>Scope = "user"</c> — returns audit rows where <c>UserId</c> equals
///             the current user (cross-tenant self-service "my login activity" view).
///             Used when the same endpoint is called by a non-admin user to inspect
///             their own auth history.
///         </item>
///     </list>
/// </remarks>
public sealed record GetAuditLogsQuery(
    string? Action = null,
    Guid? UserId = null,
    string? Entity = null,
    Guid? EntityId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Scope = "organization",
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<AuditLogDto>>;
