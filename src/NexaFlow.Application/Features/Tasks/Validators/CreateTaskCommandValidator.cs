using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Tasks.Validators;

public sealed class CreateTaskCommandValidator : AbstractValidator<Commands.CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");
        RuleFor(x => x.Description)
            .MaximumLength(5000).WithMessage("Description must not exceed 5000 characters.");
        RuleFor(x => x.Priority)
            .NotEqual(TaskPriority.None).WithMessage("Priority is required.");
    }
}
