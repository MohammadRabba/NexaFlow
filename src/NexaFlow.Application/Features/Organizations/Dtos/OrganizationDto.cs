namespace NexaFlow.Application.Features.Organizations.Dtos;

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    Guid OwnerUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record OrganizationSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    Guid OwnerUserId);
