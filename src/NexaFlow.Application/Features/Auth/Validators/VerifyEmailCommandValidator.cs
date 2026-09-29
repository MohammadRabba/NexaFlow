using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class VerifyEmailCommandValidator : AbstractValidator<Commands.VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid address.")
            .MaximumLength(320);

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Verification token is required.");
    }
}
