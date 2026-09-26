namespace Shared.Common.Errors;

public enum ErrorType
{
    Failure,             // generic 400 — malformed input that isn't a FluentValidation rule
    Validation,          // 400 — a specific field failed a rule
    Unauthorized,        // 401 — no/invalid credentials
    Forbidden,           // 403 — valid credentials, not allowed to do this
    NotFound,            // 404
    Conflict,            // 409 — current state disallows the action (includes business-rule violations)
    ServiceUnavailable,  // 503 — a dependency is down; retry later
    Unexpected,          // 500 — anything else; never describe the cause to the caller
}
