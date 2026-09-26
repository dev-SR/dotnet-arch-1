namespace Shared.Common.Errors;

public readonly record struct Success;   // marker for ErrorOr<Success> void return types

public readonly struct ErrorOr<TValue>
{
    private readonly TValue? _value;
    private readonly List<Error>? _errors;

    public bool IsError => _errors is not null;

    // Only meaningful when IsError is false. Throws otherwise — see the "why" above.
    public TValue Value => IsError
        ? throw new InvalidOperationException(
            "Cannot access Value on a failed ErrorOr. Check IsError first, or use Match.")
        : _value!;

    // Only meaningful when IsError is true. Empty list otherwise, never null,
    // so callers can safely foreach without a null check.
    public IReadOnlyList<Error> Errors => _errors ?? [];

    // First error only — a convenience for call sites that only care about one,
    // e.g. logging "the request failed because {FirstError}".
    public Error FirstError => _errors is { Count: > 0 } list
        ? list[0]
        : throw new InvalidOperationException("ErrorOr has no errors; check IsError first.");

    private ErrorOr(TValue value) => _value = value;
    private ErrorOr(List<Error> errors) => _errors = errors;

    public static ErrorOr<TValue> From(List<Error> errors) => new(errors);

    public static implicit operator ErrorOr<TValue>(TValue value) => new(value);
    public static implicit operator ErrorOr<TValue>(Error error) => new(new List<Error> { error });
    public static implicit operator ErrorOr<TValue>(List<Error> errors) => From(errors);

    // The safe way to read an ErrorOr: supply a function for each branch, and the
    // compiler-checked return type of both branches must agree. There is no way to
    // "forget" to handle the error case, unlike checking .IsError and then reading .Value.
    public TResult Match<TResult>(Func<TValue, TResult> onValue, Func<IReadOnlyList<Error>, TResult> onError) =>
        IsError ? onError(Errors) : onValue(Value);

    public async Task<TResult> MatchAsync<TResult>(
        Func<TValue, Task<TResult>> onValue, Func<IReadOnlyList<Error>, Task<TResult>> onError) =>
        IsError ? await onError(Errors) : await onValue(Value);

    // Chbody: run `next` only on success, short-circuiting on the first failure.
    // This is what lets several fallible steps compose without nested if(IsError) checks.
    public ErrorOr<TNext> Then<TNext>(Func<TValue, ErrorOr<TNext>> next) =>
        IsError ? Errors.ToList() : next(Value);

    public async Task<ErrorOr<TNext>> ThenAsync<TNext>(Func<TValue, Task<ErrorOr<TNext>>> next) =>
        IsError ? Errors.ToList() : await next(Value);
}
