using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Dtos;
using NexaFlow.Application.Features.ProjectMembers.Dtos;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

public sealed class AddProjectMemberCommandHandler : IRequestHandler<AddProjectMemberCommand, ProjectMemberDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ICacheService _cache;
    private readonly IAuditService _audit;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<AddProjectMemberCommandHandler> _logger;

    public AddProjectMemberCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ICacheService cache,
        IAuditService audit,
        IOptions<CacheOptions> cacheOptions,
        ILogger<AddProjectMemberCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _cache = cache;
        _audit = audit;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<ProjectMemberDto> Handle(AddProjectMemberCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Owner, cancellationToken);

        var invitee = await _db.FindUserByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException(
                "User not found. The user must be registered before being added to a project.",
                "USER_NOT_FOUND");

        // Phase 7: Cache-aside for user:{id} — populate cache with non-sensitive user data.
        var userDto = new UserDto(invitee.Id, invitee.Email.Value, invitee.DisplayName, invitee.EmailVerified);
        await _cache.SetAsync($"user:{invitee.Id}", userDto, _cacheOptions.UserCacheTtl, cancellationToken);

        if (invitee.Id == _currentUser.UserId)
            throw new DomainException("You are already a member of this project.", "USER_ALREADY_MEMBER");

        if (project.Members.Any(m => m.UserId == invitee.Id))
            throw new DomainException("User is already a member of this project.", "USER_ALREADY_MEMBER");

        var member = project.AddMember(invitee.Id, request.Role, DateTimeOffset.UtcNow);

        // Audit ProjectMemberAdded — same transaction.
        await _audit.RecordAsync(
            action: AuditAction.ProjectMemberAdded,
            entity: "ProjectMember",
            entityId: member.Id,
            newValues: $"{{\"projectId\":\"{project.Id}\",\"userId\":\"{invitee.Id}\",\"role\":\"{request.Role}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} added user {AddedId} to project {ProjectId} as {Role}.",
            _currentUser.UserId, invitee.Id, project.Id, request.Role);

        return new ProjectMemberDto(
            Id: member.Id, ProjectId: project.Id, UserId: member.UserId,
            Role: member.Role, JoinedAtUtc: member.CreatedAtUtc);
    }
}
