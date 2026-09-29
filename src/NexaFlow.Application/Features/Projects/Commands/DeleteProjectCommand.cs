using MediatR;

namespace NexaFlow.Application.Features.Projects.Commands;

/// <summary>
///     Soft-delete a project. Memberships are retained (CASCADE only applies to hard-delete;
/// soft-delete just stamps DeletedAtUtc). The project Owner is the only role that can delete
/// — enforced in the handler via ProjectAccess with minimumRole=Owner.
/// </summary>
public sealed record DeleteProjectCommand(Guid ProjectId) : IRequest<Unit>;
