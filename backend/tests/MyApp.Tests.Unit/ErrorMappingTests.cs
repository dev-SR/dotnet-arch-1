using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shared.Common.Errors;
using Shared.Common.Errors.Http;

namespace MyApp.Tests.Unit;

public class ErrorMappingTests
{
    [Theory]
    [InlineData(ErrorType.Failure, HttpStatusCode.BadRequest)]
    [InlineData(ErrorType.Validation, HttpStatusCode.BadRequest)]
    [InlineData(ErrorType.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(ErrorType.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(ErrorType.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorType.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ErrorType.Unexpected, HttpStatusCode.InternalServerError)]
    public void MapStatusCode_Maps_All_ErrorTypes(ErrorType type, HttpStatusCode expected)
    {
        var error = new ErrorProbe(type);
        ErrorExtensions.MapStatusCode(error.Value).Should().Be(expected);
    }

    [Fact]
    public async Task ToProblem_ValidationList_Groups_By_Field()
    {
        var errors = new List<Error>
        {
            Error.ValidationField("FirstName", "required"),
            Error.ValidationField("FirstName", "too long"),
            Error.ValidationField("Email", "invalid"),
        };

        var (status, json) = await ExecuteProblemAsync((ctx, t) => errors.ToProblem(ctx, t));

        status.Should().Be(400);
        json.GetProperty("errorCode").GetString().Should().Be("VALIDATION");
        json.GetProperty("errors").GetProperty("FirstName").GetArrayLength().Should().Be(2);
        json.GetProperty("errors").GetProperty("Email").GetArrayLength().Should().Be(1);
        json.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ToProblem_MixedList_NonValidation_Wins()
    {
        var errors = new List<Error>
        {
            Error.ValidationField("Name", "required"),
            Error.Forbidden("DIAG.FORBIDDEN", "Not allowed."),
        };

        var (status, json) = await ExecuteProblemAsync((ctx, t) => errors.ToProblem(ctx, t));

        status.Should().Be(403);
        json.GetProperty("errorCode").GetString().Should().Be("DIAG.FORBIDDEN");
        json.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ToProblem_Unexpected_Redacts_Detail()
    {
        var error = Error.Unexpected("X", "secret internal detail");
        var (status, json) = await ExecuteProblemAsync((ctx, t) => error.ToProblem(ctx, t));

        status.Should().Be(500);
        json.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        json.GetProperty("detail").GetString().Should().NotContain("secret");
    }

    [Fact]
    public async Task ToProblem_EmptyList_Returns500()
    {
        IReadOnlyList<Error> errors = [];
        var (status, json) = await ExecuteProblemAsync((ctx, t) => errors.ToProblem(ctx, t));

        status.Should().Be(500);
        json.GetProperty("errorCode").GetString().Should().Be("EMPTY_ERROR_LIST");
    }

    private static async Task<(int Status, JsonElement Json)> ExecuteProblemAsync(
        Func<HttpContext, string?, IResult> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(_ => { });
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider };
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "unit-trace";

        var result = factory(ctx, null);
        await result.ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc.RootElement.Clone());
    }

    /// <summary>Builds an Error for each ErrorType without exposing a public ctor.</summary>
    private sealed class ErrorProbe
    {
        public Error Value { get; }

        public ErrorProbe(ErrorType type)
        {
            Value = type switch
            {
                ErrorType.Failure => Error.Failure("C", "d"),
                ErrorType.Validation => Error.Validation("C", "d"),
                ErrorType.Unauthorized => Error.Unauthorized("C", "d"),
                ErrorType.Forbidden => Error.Forbidden("C", "d"),
                ErrorType.NotFound => Error.NotFound("C", "d"),
                ErrorType.Conflict => Error.Conflict("C", "d"),
                ErrorType.ServiceUnavailable => Error.ServiceUnavailable("d"),
                ErrorType.Unexpected => Error.Unexpected("C", "d"),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }
    }
}
