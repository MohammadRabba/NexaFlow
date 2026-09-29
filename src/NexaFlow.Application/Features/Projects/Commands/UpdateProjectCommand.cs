using MediatR;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Projects.Commands;

/// <summary>
///     Update a project's editable fields. Each field is optional — null means "leave unchanged".
/// For dates, pass a <see cref="DatesUpdate" /> record; null inside the record means "clear that date".
/// </summary>
public sealed record UpdateProjectCommand(
    Guid ProjectId,
    string? NewName,
    string? NewDescription,
    ProjectStatus? NewStatus,
    DatesUpdate? Dates) : IRequest<Unit>;

/// <summary>
///     Carrier for date changes. Both fields nullable — null means "clear".
/// </summary>
public sealed record DatesUpdate(DateTimeOffset? StartDateUtc, DateTimeOffset? DueDateUtc);
