namespace Shared.Common.Errors;

public readonly struct Error : IEquatable<Error>
{
    public const string ServiceUnavailableDefaultCode = "SERVICE_UNAVAILABLE";

    public string Code { get; }
    public string Description { get; }
    public ErrorType Type { get; }
    public IReadOnlyDictionary<string, object>? Metadata { get; }

    private Error(string code, string description, ErrorType type, IReadOnlyDictionary<string, object>? metadata)
    {
        // Fail fast: an error with no code can't be told apart from any other error of
        // the same Type once it reaches a log line or a client integration test.
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Code = code;
        Description = description;
        Type = type;
        Metadata = metadata;
    }

    public static Error Failure(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Failure, m);

    public static Error Validation(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Validation, m);

    public static Error Unauthorized(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Unauthorized, m);

    public static Error Forbidden(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Forbidden, m);

    public static Error NotFound(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.NotFound, m);

    public static Error Conflict(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Conflict, m);

    public static Error ServiceUnavailable(string description, string code = ServiceUnavailableDefaultCode) =>
        new(code, description, ErrorType.ServiceUnavailable, null);

    public static Error Unexpected(string code, string description, IReadOnlyDictionary<string, object>? m = null) =>
        new(code, description, ErrorType.Unexpected, m);

    // Convenience for "this one field failed one rule" — code and field name are the same string,
    // which is exactly what FluentValidation's PropertyName gives you (section 4).
    public static Error ValidationField(string propertyName, string message) =>
        Validation(propertyName, message);

    public bool Equals(Error other) => Code == other.Code && Type == other.Type && Description == other.Description;
    public override bool Equals(object? obj) => obj is Error e && Equals(e);
    public override int GetHashCode() => HashCode.Combine(Code, Type, Description);
    public override string ToString() => $"{Type} {Code}: {Description}";

    public static bool operator ==(Error left, Error right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Error left, Error right)
    {
        return !(left == right);
    }
}
