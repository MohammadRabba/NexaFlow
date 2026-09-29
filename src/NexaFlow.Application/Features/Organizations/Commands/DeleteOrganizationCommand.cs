using MediatR;

namespace NexaFlow.Application.Features.Organizations.Commands;

/// <summary>
///     Soft-delete an organization. The org row remains in the DB (for audit / referential
///     integrity), but is excluded from the user's organization list via the DeletedAtUtc
///     filter. Memberships of a soft-deleted org are also excluded by the join condition.
/// </summary>
public sealed record DeleteOrganizationCommand(Guid OrganizationId) : IRequest<Unit>;
