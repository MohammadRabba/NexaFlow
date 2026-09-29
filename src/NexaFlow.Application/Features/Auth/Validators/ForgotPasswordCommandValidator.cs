using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<Commands.ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid address.")
            .MaximumLength(320);
    }
}
