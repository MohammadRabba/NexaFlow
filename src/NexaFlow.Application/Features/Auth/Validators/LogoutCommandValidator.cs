using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class LogoutCommandValidator : AbstractValidator<Commands.LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
    }
}
