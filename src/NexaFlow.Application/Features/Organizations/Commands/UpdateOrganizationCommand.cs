using MediatR;

namespace NexaFlow.Application.Features.Organizations.Commands;

/// <summary>
///     Rename an organization. The slug is NOT changed — it's an immutable URL identifier
///     (renaming a slug would break every external link to the org).
/// </summary>
public sealed record UpdateOrganizationCommand(
    Guid OrganizationId,
    string NewName) : IRequest<Unit>;
