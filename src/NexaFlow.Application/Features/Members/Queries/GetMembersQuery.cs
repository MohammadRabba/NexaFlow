using MediatR;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Members.Dtos;

namespace NexaFlow.Application.Features.Members.Queries;

public sealed record GetMembersQuery(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<MemberDto>>;
