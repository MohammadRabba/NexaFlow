using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NexaFlow.Api.Hubs;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly INotificationPusher _pusher;

    public NotificationsController(IMediator mediator, INotificationPusher pusher)
    {
        _mediator = mediator;
        _pusher = pusher;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var uid))
            return Unauthorized();
        var result = await _mediator.Send(new GetNotificationsQuery(uid, unreadOnly, page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsReadAsync(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var uid))
            return Unauthorized();
        await _mediator.Send(new MarkNotificationAsReadCommand(id, uid), ct);
        return NoContent();
    }
}

public sealed record GetNotificationsQuery(Guid UserId, bool UnreadOnly, int Page, int PageSize) : IRequest<List<object>>;
public sealed record MarkNotificationAsReadCommand(Guid NotificationId, Guid UserId) : IRequest<Unit>;

public sealed class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, List<object>>
{
    private readonly IApplicationDbContext _db;
    public GetNotificationsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<object>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);
        var items = await _db.GetNotificationsForUserAsync(request.UserId, request.UnreadOnly, page, pageSize, cancellationToken);
        return items.Cast<object>().ToList();
    }
}

public sealed class MarkNotificationAsReadCommandHandler : IRequestHandler<MarkNotificationAsReadCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    public MarkNotificationAsReadCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(MarkNotificationAsReadCommand request, CancellationToken cancellationToken)
    {
        var notif = await _db.FindNotificationAsync(request.NotificationId, request.UserId, cancellationToken)
            ?? throw new Domain.Exceptions.NotFoundException("Notification", request.NotificationId);
        notif.MarkAsRead(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
