using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Dtos;

public sealed record ProjectMemberDto(
    Guid Id,
    Guid ProjectId,
    Guid UserId,
    ProjectMemberRole Role,
    DateTimeOffset JoinedAtUtc);

public sealed record InviteProjectMemberSummary(
    Guid ProjectId,
    Guid UserId,
    string Token,
    DateTimeOffset ExpiresAtUtc);
