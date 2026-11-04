namespace TraderRecords.Api.Application.Common;

/// <summary>
/// Lightweight result wrapper for expected failures. The private constructors
/// preserve the invariant that a result has either a value or an error, never both.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _error = error;
    }

    public bool IsSuccess { get; }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("A failed result has no value.");

    public Error Error =>
        !IsSuccess
            ? _error!
            : throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);
}
