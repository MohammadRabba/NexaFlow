using MediatR;
using NexaFlow.Application.Features.Projects.Dtos;

namespace NexaFlow.Application.Features.Projects.Commands;

/// <summary>
///     Create a new project in the resolved tenant. The current user becomes the project Owner.
///     The tenant is taken from the resolved context (X-Organization-Id header, validated
///     against the user's org membership); the request body does NOT carry organizationId.
/// </summary>
public sealed record CreateProjectCommand(
    string Name,
    string Description,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? DueDateUtc) : IRequest<ProjectDto>;
