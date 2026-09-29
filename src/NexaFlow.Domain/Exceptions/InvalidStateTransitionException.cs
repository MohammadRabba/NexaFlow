namespace NexaFlow.Domain.Exceptions;

/// <summary>
///     Raised when an aggregate invariant is violated by an attempted state
///     transition (e.g., completing an already-cancelled task — section 17).
/// </summary>
public class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entityName, string fromState, string toState)
        : base(
            $"Invalid state transition for {entityName}: '{fromState}' -> '{toState}' is not allowed.",
            "INVALID_STATE_TRANSITION")
    {
        EntityName = entityName;
        FromState = fromState;
        ToState = toState;
    }

    public string EntityName { get; }
    public string FromState { get; }
    public string ToState { get; }
}
