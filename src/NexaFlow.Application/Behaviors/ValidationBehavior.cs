using FluentValidation;
using MediatR;

namespace NexaFlow.Application.Behaviors;

/// <summary>
///     Cross-cutting pipeline behavior that runs the appropriate FluentValidation
///     validator(s) for a MediatR request BEFORE the handler executes (section 13 —
///     "Validation belongs primarily in Application"). On failure, throws
///     <see cref="ValidationException" />, which the API layer maps to a 422 Problem
///     Details response (section 31).
///     <para>
///         For Phase 1, this behavior is wired up but no validators are registered yet
///         — handlers don't exist until Phase 2+. The behavior is a no-op when no
///         validators exist for a request type.
///     </para>
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
