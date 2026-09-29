using FluentValidation;

namespace NexaFlow.Application.Features.Projects.Validators;

public sealed class CreateProjectCommandValidator : AbstractValidator<Commands.CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(100).WithMessage("Project name must not exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");

        // Date consistency is enforced by the domain's SetDates too; here we provide
        // a friendlier 422 to the client.
        RuleFor(x => x)
            .Must(c => !(c.StartDateUtc.HasValue && c.DueDateUtc.HasValue) || c.DueDateUtc >= c.StartDateUtc)
            .WithMessage("Due date must be on or after start date.");
    }
}
