namespace Shipment.Domain.Common;

/// <summary>What kind of refusal: the API answers 400, 404 or 409, a page shows the message.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
}

/// <summary>A predictable refusal - written for the person who reads it (English, a sentence).</summary>
public sealed record Error(ErrorKind Kind, string Message)
{
    public static Error Validation(string message) => new(ErrorKind.Validation, message);

    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    /// <summary>The row version the caller sent is not the current one.</summary>
    public static Error Stale(string what) =>
        Conflict($"{what} was changed by someone else. Reload it and try again.");
}

/// <summary>Success or an <see cref="Error"/>; exceptions stay for bugs and infrastructure.</summary>
public class Result
{
    public static readonly Result Success = new(null);

    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static implicit operator Result(Error error) => new(error);
}

/// <summary>A value or an <see cref="Error"/>.</summary>
public sealed class Result<T> : Result
{
    // ReSharper disable once ReplaceWithFieldKeyword - the value is private, Value guards reading it from a failure.
    private readonly T? _value;

    private Result(T value)
        : base(null) => _value = value;

    private Result(Error error)
        : base(error) { }

    /// <summary>The value; reading it from a failure is a bug.</summary>
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException($"No value: {Error!.Message}");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
