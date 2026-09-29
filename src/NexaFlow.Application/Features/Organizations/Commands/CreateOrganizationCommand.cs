using MediatR;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Commands;

/// <summary>
///     Create a new organization. The current user automatically becomes the Owner.
///     The organization's slug is generated server-side (lowercase, kebab-cased) —
///     the client supplies only name + an optional slug suggestion.
/// </summary>
public sealed record CreateOrganizationCommand(
    string Name,
    string? SlugSuggestion) : IRequest<OrganizationDto>;
