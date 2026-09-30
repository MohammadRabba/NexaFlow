using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.AuditLogs.Dtos;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.AuditLogs.Queries;

public sealed class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public GetAuditLogsQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public async Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        // Scope validation: only "organization" and "user" are supported.
        var scope = (request.Scope ?? "organization").ToLowerInvariant();
        if (scope != "organization" && scope != "user")
            throw new DomainException("Scope must be 'organization' or 'user'.", "INVALID_SCOPE");

        List<Domain.Entities.AuditLog> items;
        long total;

        if (scope == "user")
        {
            // Self-service view: only the current user's own rows. The handler does NOT
            // trust a client-supplied UserId — it always uses the ambient authenticated
            // user id. (Spec §49: do not trust client-provided values when the trusted
            // context can derive them.)
            (items, total) = await _db.GetPagedAuditLogsForUserAsync(
                userId,
                request.Action,
                request.FromUtc,
                request.ToUtc,
                page,
                pageSize,
                cancellationToken);
        }
        else
        {
            // Organization-scoped view: requires resolved tenant AND AuditLog.Read permission.
            // The [Authorize(Policy = Permissions.AuditLogRead)] attribute on the controller
            // already enforces the permission via the role→permission mapping. The handler
            // additionally verifies the tenant is resolved — defense in depth.
            if (!_currentTenant.IsTenantResolved)
                throw new DomainException("Tenant must be resolved for organization-scoped audit log queries.", "TENANT_NOT_RESOLVED");

            var organizationId = _currentTenant.RequireTenantId();

            (items, total) = await _db.GetPagedAuditLogsAsync(
                organizationId,
                request.Action,
                request.UserId,
                request.Entity,
                request.EntityId,
                request.FromUtc,
                request.ToUtc,
                page,
                pageSize,
                cancellationToken);
        }

        var dtos = items.Select(a => new AuditLogDto(
            a.Id,
            a.UserId,
            a.OrganizationId,
            a.Action,
            a.Entity,
            a.EntityId,
            a.OldValues,
            a.NewValues,
            a.IPAddress,
            a.Timestamp)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<AuditLogDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
