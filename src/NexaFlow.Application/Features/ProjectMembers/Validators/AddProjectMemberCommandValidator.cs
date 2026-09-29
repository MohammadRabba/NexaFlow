using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Validators;

public sealed class AddProjectMemberCommandValidator : AbstractValidator<Commands.AddProjectMemberCommand>
{
    public AddProjectMemberCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Role)
            .Must(r => r is ProjectMemberRole.Contributor or ProjectMemberRole.Reader)
            .WithMessage("AddMember can only assign Contributor or Reader. Use transfer-ownership to set Owner.");
    }
}
