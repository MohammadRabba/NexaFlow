using MediatR;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Queries;

/// <summary>
///     Page the organizations the current user belongs to. The result is scoped server-side
///     to the user's memberships — the client cannot request "all organizations".
/// </summary>
public sealed record GetOrganizationsQuery(int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<OrganizationSummaryDto>>;
