using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Queries;

public sealed class GetOrganizationQueryHandler : IRequestHandler<GetOrganizationQuery, OrganizationDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetOrganizationQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<OrganizationDto?> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            // Unauthenticated — return null; the controller translates to 401 via [Authorize].
            return null;
        }

        // Load the org AND its members — we need to verify the current user is a member.
        var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken);
        if (org is null || org.IsDeleted) return null;

        // Cross-tenant leak avoidance: if the user is not a member, return null (→ 404),
        // not a 403 — same response as "doesn't exist".
        var isMember = org.Members.Any(m => m.UserId == userId && m.IsActive);
        if (!isMember) return null;

        return new OrganizationDto(
            Id: org.Id,
            Name: org.Name,
            Slug: org.Slug,
            OwnerUserId: org.OwnerUserId,
            CreatedAtUtc: org.CreatedAtUtc);
    }
}
