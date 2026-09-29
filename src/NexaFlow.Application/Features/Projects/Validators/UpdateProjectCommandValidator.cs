using FluentValidation;

namespace NexaFlow.Application.Features.Projects.Validators;

public sealed class UpdateProjectCommandValidator : AbstractValidator<Commands.UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("ProjectId is required.");

        RuleFor(x => x.NewName)
            .MaximumLength(100).WithMessage("Project name must not exceed 100 characters.")
            .When(x => x.NewName is not null);

        RuleFor(x => x.NewDescription)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(x => x.NewDescription is not null);

        // Date consistency — the domain also checks this, but a friendly 422 here is better UX.
        RuleFor(x => x)
            .Must(c => c.Dates is null
                || !(c.Dates.StartDateUtc.HasValue && c.Dates.DueDateUtc.HasValue)
                || c.Dates.DueDateUtc >= c.Dates.StartDateUtc)
            .WithMessage("Due date must be on or after start date.");
    }
}
