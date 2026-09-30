using System.Diagnostics.CodeAnalysis;
using Chirograph.Domain.Common;

namespace Chirograph.Application.Common;

/// <summary>An expected failure. <see cref="Message"/> is written for end users.</summary>
public sealed record Error(string Code, string Message)
{
    public static Error From(DomainException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new Error(exception.Code, exception.Message);
    }
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(null) => _value = value;

    private Result(Error error)
        : base(error)
    {
    }

    public T Value => Succeeded ? _value! : throw new InvalidOperationException($"No value: {Error.Code}");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
