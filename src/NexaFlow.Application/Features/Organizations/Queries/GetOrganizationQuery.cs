using MediatR;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Queries;

/// <summary>
///     Get a single organization by id. Returns null (translated to 404 by the controller)
///     if the org doesn't exist OR the current user is not a member (cross-tenant
///     leak-avoidance — same response for "doesn't exist" and "exists but not yours").
/// </summary>
public sealed record GetOrganizationQuery(Guid OrganizationId) : IRequest<OrganizationDto?>;
