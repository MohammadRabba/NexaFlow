using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Members.Validators;

public sealed class UpdateMemberRoleCommandValidator : AbstractValidator<Commands.UpdateMemberRoleCommand>
{
    public UpdateMemberRoleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(Guid.Empty);
        RuleFor(x => x.NewRole)
            .Must(r => r is OrganizationRole.Admin or OrganizationRole.Member or OrganizationRole.Viewer)
            .WithMessage("NewRole must be Admin, Member, or Viewer. Use ownership transfer to set Owner.");
    }
}
