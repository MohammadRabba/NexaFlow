using FluentValidation;

namespace NexaFlow.Application.Features.ProjectMembers.Validators;

public sealed class TransferProjectOwnershipCommandValidator : AbstractValidator<Commands.TransferProjectOwnershipCommand>
{
    public TransferProjectOwnershipCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.ToUserId).NotEqual(Guid.Empty);
    }
}
