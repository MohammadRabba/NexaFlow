namespace NexaFlow.Application.Common;

/// <summary>
///     Operation result without a value — either success or a typed failure.
///     Used by command handlers so they can return a domain-level failure that
///     the controller maps to a 4xx Problem Details response without throwing.
/// </summary>
public readonly record struct Result
{
    public bool IsSuccess { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);

    public void Deconstruct(out bool isSuccess, out string? errorCode, out string? errorMessage)
    {
        isSuccess = IsSuccess;
        errorCode = ErrorCode;
        errorMessage = ErrorMessage;
    }
}

/// <summary>Typed variant of <see cref="Result" /> for queries that need to return data.</summary>
public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string errorCode, string errorMessage) =>
        new(false, default, errorCode, errorMessage);
}
