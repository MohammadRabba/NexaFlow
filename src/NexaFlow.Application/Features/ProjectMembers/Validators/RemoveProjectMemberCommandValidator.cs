using FluentValidation;

namespace NexaFlow.Application.Features.ProjectMembers.Validators;

public sealed class RemoveProjectMemberCommandValidator : AbstractValidator<Commands.RemoveProjectMemberCommand>
{
    public RemoveProjectMemberCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(Guid.Empty);
    }
}
