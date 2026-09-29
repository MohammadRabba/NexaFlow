namespace NexaFlow.Domain.Enums;

public enum TaskItemStatus
{
    None = 0,
    Todo = 1,
    InProgress = 2,
    Review = 3,
    Done = 4,
    Cancelled = 5
}

public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public static class TaskItemStatusExtensions
{
    private static readonly Dictionary<TaskItemStatus, HashSet<TaskItemStatus>> AllowedTransitions = new()
    {
        [TaskItemStatus.Todo] = [TaskItemStatus.InProgress, TaskItemStatus.Cancelled],
        [TaskItemStatus.InProgress] = [TaskItemStatus.Review, TaskItemStatus.Done, TaskItemStatus.Cancelled],
        [TaskItemStatus.Review] = [TaskItemStatus.InProgress, TaskItemStatus.Done, TaskItemStatus.Cancelled],
        [TaskItemStatus.Done] = [TaskItemStatus.InProgress],
        [TaskItemStatus.Cancelled] = [TaskItemStatus.Todo],
        [TaskItemStatus.None] = []
    };

    public static bool CanTransitionTo(this TaskItemStatus from, TaskItemStatus to)
    {
        if (from == to) return false;
        if (to == TaskItemStatus.None) return false;
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
