using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class ChangePasswordCommandValidator : AbstractValidator<Commands.ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty).WithMessage("User id is required.");

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.")
            .Must(HaveAtLeastOneDigit).WithMessage("Password must contain at least one digit.")
            .Must(HaveAtLeastOneUppercase).WithMessage("Password must contain at least one uppercase letter.")
            .Must(HaveAtLeastOneLowercase).WithMessage("Password must contain at least one lowercase letter.")
            .Must((cmd, p) => p != cmd.CurrentPassword).WithMessage("New password must be different from current password.");
    }

    private static bool HaveAtLeastOneDigit(string p) => p.Any(char.IsDigit);
    private static bool HaveAtLeastOneUppercase(string p) => p.Any(char.IsUpper);
    private static bool HaveAtLeastOneLowercase(string p) => p.Any(char.IsLower);
}
