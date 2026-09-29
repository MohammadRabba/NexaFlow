using FluentValidation;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Members.Validators;

public sealed class InviteMemberCommandValidator : AbstractValidator<Commands.InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEqual(Guid.Empty).WithMessage("OrganizationId is required.");

        RuleFor(x => x.InviteeEmail)
            .NotEmpty().WithMessage("Invitee email is required.")
            .EmailAddress().WithMessage("Invitee email is not a valid address.");

        RuleFor(x => x.Role)
            .Must(r => r is OrganizationRole.Admin or OrganizationRole.Member or OrganizationRole.Viewer)
            .WithMessage("Invitations can only assign Admin, Member, or Viewer roles. Owner is set via ownership transfer.");
    }
}
