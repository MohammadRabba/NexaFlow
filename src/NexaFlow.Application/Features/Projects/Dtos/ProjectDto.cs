using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Description,
    ProjectStatus Status,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? DueDateUtc,
    Guid OwnerUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectSummaryDto(
    Guid Id,
    string Name,
    ProjectStatus Status,
    Guid OwnerUserId,
    DateTimeOffset? DueDateUtc);
