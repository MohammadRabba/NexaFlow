using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class RefreshCommandValidator : AbstractValidator<Commands.RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}
