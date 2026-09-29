using MediatR;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Projects.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Projects.Queries;

/// <summary>
///     Page the projects in the resolved tenant that the current user is a member of.
/// Filters: status (optional), name search (case-insensitive substring). Sort: name,
/// status, dueDate, createdAt (default: createdAt desc).
/// </summary>
public sealed record GetProjectsQuery(
    ProjectStatus? Status = null,
    string? Search = null,
    string? SortBy = null,
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProjectSummaryDto>>;
