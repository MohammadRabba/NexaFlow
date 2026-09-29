using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Validators;

public sealed class UpdateProjectMemberRoleCommandValidator : AbstractValidator<Commands.UpdateProjectMemberRoleCommand>
{
    public UpdateProjectMemberRoleCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(Guid.Empty);
        RuleFor(x => x.NewRole)
            .Must(r => r is ProjectMemberRole.Contributor or ProjectMemberRole.Reader)
            .WithMessage("NewRole must be Contributor or Reader. Use transfer-ownership to set Owner.");
    }
}
