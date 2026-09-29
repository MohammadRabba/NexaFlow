using FluentValidation;

namespace NexaFlow.Application.Features.Auth.Validators;

public sealed class ResetPasswordCommandValidator : AbstractValidator<Commands.ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid address.")
            .MaximumLength(320);

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.")
            .Must(HaveAtLeastOneDigit).WithMessage("Password must contain at least one digit.")
            .Must(HaveAtLeastOneUppercase).WithMessage("Password must contain at least one uppercase letter.")
            .Must(HaveAtLeastOneLowercase).WithMessage("Password must contain at least one lowercase letter.")
            .Must((cmd, p) => !p.Contains(cmd.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password must not contain the email address.");
    }

    private static bool HaveAtLeastOneDigit(string p) => p.Any(char.IsDigit);
    private static bool HaveAtLeastOneUppercase(string p) => p.Any(char.IsUpper);
    private static bool HaveAtLeastOneLowercase(string p) => p.Any(char.IsLower);
}
