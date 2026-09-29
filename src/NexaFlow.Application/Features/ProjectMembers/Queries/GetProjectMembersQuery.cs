using MediatR;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.ProjectMembers.Dtos;

namespace NexaFlow.Application.Features.ProjectMembers.Queries;

public sealed record GetProjectMembersQuery(
    Guid ProjectId,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProjectMemberDto>>;
