using FluentValidation;

namespace NexaFlow.Application.Features.Organizations.Validators;

public sealed class UpdateOrganizationCommandValidator : AbstractValidator<Commands.UpdateOrganizationCommand>
{
    public UpdateOrganizationCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEqual(Guid.Empty).WithMessage("OrganizationId is required.");
        RuleFor(x => x.NewName)
            .NotEmpty().WithMessage("New name is required.")
            .MaximumLength(100).WithMessage("Organization name must not exceed 100 characters.");
    }
}
