using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Members.Dtos;

public sealed record MemberDto(
    Guid Id,
    Guid UserId,
    string? Email,           // populated via join with Users (read model)
    string? DisplayName,     // populated via join with Users
    OrganizationRole Role,
    bool IsActive,
    DateTimeOffset JoinedAtUtc);

public sealed record InviteSummaryDto(
    Guid OrganizationId,
    Guid UserId,
    string Token,            // plaintext — returned ONCE; never stored
    DateTimeOffset ExpiresAtUtc);
