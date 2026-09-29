using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Tasks.Validators;

public sealed class UpdateTaskCommandValidator : AbstractValidator<Commands.UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
        RuleFor(x => x.NewTitle)
            .NotEmpty().When(x => x.NewTitle is not null)
            .MaximumLength(200).When(x => x.NewTitle is not null);
        RuleFor(x => x.NewDescription)
            .MaximumLength(5000).When(x => x.NewDescription is not null);
        RuleFor(x => x.NewPriority)
            .NotEqual(TaskPriority.None).When(x => x.NewPriority.HasValue);
    }
}

public sealed class ChangeTaskStatusCommandValidator : AbstractValidator<Commands.ChangeTaskStatusCommand>
{
    public ChangeTaskStatusCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
        RuleFor(x => x.NewStatus).NotEqual(TaskItemStatus.None);
    }
}

public sealed class AssignTaskCommandValidator : AbstractValidator<Commands.AssignTaskCommand>
{
    public AssignTaskCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
    }
}

public sealed class DeleteTaskCommandValidator : AbstractValidator<Commands.DeleteTaskCommand>
{
    public DeleteTaskCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
    }
}
