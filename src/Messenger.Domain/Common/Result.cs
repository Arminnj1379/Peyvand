namespace Messenger.Domain.Common;

/// <summary>
/// Explicit success/failure result. Domain failures are returned, not thrown.
/// The API layer maps failures to RFC 9457 problem details.
/// </summary>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, Error error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error Error { get; }

    public static Result<T> Ok(T value) => new(true, value, Error.None);

    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}

/// <summary>Non-generic result for operations without a return value.</summary>
public sealed class Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Ok() => new(true, Error.None);

    public static Result Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}
