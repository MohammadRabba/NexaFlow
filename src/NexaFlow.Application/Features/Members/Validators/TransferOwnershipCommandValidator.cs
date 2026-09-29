using FluentValidation;

namespace NexaFlow.Application.Features.Members.Validators;

public sealed class TransferOwnershipCommandValidator : AbstractValidator<Commands.TransferOwnershipCommand>
{
    public TransferOwnershipCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEqual(Guid.Empty);
        RuleFor(x => x.ToUserId).NotEqual(Guid.Empty);
    }
}
