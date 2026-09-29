using MediatR;
using NexaFlow.Application.Features.Projects.Dtos;

namespace NexaFlow.Application.Features.Projects.Queries;

/// <summary>
///     Get a single project. Returns null (→ 404) if not found OR if the user is not a member
/// of this project OR if the project belongs to a different tenant. Same response shape for
/// all three to avoid enumeration leaks.
/// </summary>
public sealed record GetProjectQuery(Guid ProjectId) : IRequest<ProjectDto?>;
