using Shared.Common.Errors.Http;

namespace MyApp.Features.Diagnostics;

/// <summary>
/// Development/Testing-only routes that force each error channel for manual + integration tests.
/// </summary>
public sealed class ErrorDiagnosticsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
            return;

        var group = app.MapGroup("/_diag")
            .WithTags("Diagnostics")
            .ExcludeFromDescription();

        group.MapGet("/result/{kind}", (string kind, HttpContext http) =>
        {
            ErrorOr<string> result = kind switch
            {
                "ok" => "fine",
                "validation" => Error.ValidationField("Name", "Name is required."),
                "validation-multi" => new List<Error>
                {
                    Error.ValidationField("FirstName", "First name is required."),
                    Error.ValidationField("FirstName", "First name is too long."),
                    Error.ValidationField("Email", "Email is invalid."),
                },
                "mixed" => new List<Error>
                {
                    Error.ValidationField("Name", "Name is required."),
                    Error.Forbidden("DIAG.FORBIDDEN", "Not allowed."),
                },
                "not-found" => Error.NotFound("DIAG.NOT_FOUND", "Thing 42 was not found."),
                "conflict" => Error.Conflict("DIAG.CONFLICT", "State conflict."),
                "unauthorized" => Error.Unauthorized("DIAG.UNAUTHORIZED", "Sign in first."),
                "forbidden" => Error.Forbidden("DIAG.FORBIDDEN", "Not allowed."),
                "unavailable" => Error.ServiceUnavailable("Payment provider is down.", "PAYMENTS"),
                "failure" => Error.Failure("DIAG.FAILURE", "Generic failure."),
                "unexpected" => Error.Unexpected("DIAG.UNEXPECTED", "secret internal detail"),
                _ => Error.NotFound("DIAG.UNKNOWN_KIND", $"Unknown kind '{kind}'."),
            };
            return result.MatchOk(http);
        });

        group.MapGet("/throw/{kind}", (string kind) => Throw(kind));

        group.MapPost("/body", (DiagBody body) => Results.Ok(body));
    }

    private static IResult Throw(string kind) => throw kind switch
    {
        "unexpected" => new InvalidOperationException("secret internal detail"),
        "argument" => new ArgumentException("programmer error"),
        _ => new Exception($"Unknown throw kind '{kind}'."),
    };
}

public sealed record DiagBody(string Name, int Age);
