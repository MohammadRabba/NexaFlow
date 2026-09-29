using FluentValidation;

namespace NexaFlow.Application.Features.Organizations.Validators;

public sealed class CreateOrganizationCommandValidator : AbstractValidator<Commands.CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required.")
            .MaximumLength(100).WithMessage("Organization name must not exceed 100 characters.");

        RuleFor(x => x.SlugSuggestion)
            .MaximumLength(60).WithMessage("Slug suggestion must not exceed 60 characters.")
            .Matches("^[a-z0-9-]*$").When(x => !string.IsNullOrEmpty(x.SlugSuggestion))
            .WithMessage("Slug may only contain lowercase letters, digits, and hyphens.");
    }
}
