using FluentValidation;

namespace NexaFlow.Application.Features.Projects.Validators;

public sealed class DeleteProjectCommandValidator : AbstractValidator<Commands.DeleteProjectCommand>
{
    public DeleteProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
    }
}
