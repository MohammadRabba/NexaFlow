using FluentValidation;

namespace NexaFlow.Application.Features.Members.Validators;

public sealed class RemoveMemberCommandValidator : AbstractValidator<Commands.RemoveMemberCommand>
{
    public RemoveMemberCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(Guid.Empty);
    }
}
