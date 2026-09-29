using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Features.Labels.Commands;

public sealed record GetLabelsQuery(Guid OrganizationId) : IRequest<List<Label>>;

public sealed class GetLabelsQueryHandler : IRequestHandler<GetLabelsQuery, List<Label>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentTenantService _currentTenant;

    public GetLabelsQueryHandler(IApplicationDbContext db, ICurrentTenantService currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    public async Task<List<Label>> Handle(GetLabelsQuery request, CancellationToken cancellationToken)
    {
        var orgId = _currentTenant.RequireTenantId();
        return await _db.GetLabelsForOrganizationAsync(orgId, cancellationToken);
    }
}
