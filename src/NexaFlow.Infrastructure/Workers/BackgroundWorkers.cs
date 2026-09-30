using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Workers;

public sealed class DeadlineReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<DeadlineReminderWorker> _logger;

    public DeadlineReminderWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<DeadlineReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeadlineReminderWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTimeOffset.UtcNow;
                var cutoff = now.AddHours(24);

                var upcomingTasks = await db.Tasks
                    .IgnoreQueryFilters()
                    .Where(t => !t.IsDeleted
                        && t.DueDateUtc.HasValue
                        && t.DueDateUtc <= cutoff
                        && t.Status != TaskItemStatus.Done
                        && t.Status != TaskItemStatus.Cancelled
                        && t.AssigneeId.HasValue)
                    .ToListAsync(stoppingToken);

                foreach (var task in upcomingTasks)
                {
                    var existing = await db.Notifications
                        .AnyAsync(n => n.RelatedEntityId == task.Id
                            && n.NotificationType == "DeadlineApproaching"
                            && n.RecipientUserId == task.AssigneeId
                            && n.CreatedAtUtc > now.AddHours(-24), stoppingToken);

                    if (existing) continue;

                    var notif = Domain.Entities.Notification.Create(
                        organizationId: task.OrganizationId,
                        recipientUserId: task.AssigneeId!.Value,
                        notificationType: "DeadlineApproaching",
                        title: $"Deadline approaching: {task.Title}",
                        message: $"Task '{task.Title}' is due {task.DueDateUtc:yyyy-MM-dd HH:mm} UTC.",
                        relatedEntityId: task.Id,
                        relatedEntityType: "Task",
                        sourceEventId: null,
                        atUtc: now);
                    db.Add(notif);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeadlineReminderWorker error.");
            }

            await Task.Delay(_options.DeadlineReminderInterval, stoppingToken);
        }
    }
}

public sealed class TokenCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<TokenCleanupWorker> _logger;

    public TokenCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<TokenCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TokenCleanupWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
                var expired = await db.RefreshTokens
                    .Where(t => t.ExpiresAtUtc < DateTimeOffset.UtcNow
                        && t.RevokedAtUtc.HasValue
                        && t.RevokedAtUtc < cutoff)
                    .ToListAsync(stoppingToken);

                if (expired.Count > 0)
                {
                    db.RefreshTokens.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("TokenCleanupWorker removed {Count} expired tokens.", expired.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TokenCleanupWorker error.");
            }

            await Task.Delay(_options.TokenCleanupInterval, stoppingToken);
        }
    }
}

public sealed class WorkerOptions
{
    public TimeSpan DeadlineReminderInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan TokenCleanupInterval { get; set; } = TimeSpan.FromHours(24);
}
