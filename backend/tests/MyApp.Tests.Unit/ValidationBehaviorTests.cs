using FluentAssertions;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Behaviors;
using Shared.Common.Errors;

namespace MyApp.Tests.Unit;

public class ValidationBehaviorTests
{
    private sealed record ProbeRequest(string Name) : IRequest<ErrorOr<string>>;

    private sealed class ProbeValidator : AbstractValidator<ProbeRequest>
    {
        public ProbeValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MinimumLength(3);
        }
    }

    [Fact]
    public async Task Invalid_ShortCircuits_Handler()
    {
        var called = false;
        var behavior = CreateBehavior(new ProbeValidator());

        RequestHandlerDelegate<ErrorOr<string>> next = _ =>
        {
            called = true;
            return ValueTask.FromResult<ErrorOr<string>>("ok");
        };

        var result = await behavior.Handle(new ProbeRequest(""), next, CancellationToken.None);

        called.Should().BeFalse();
        result.IsError.Should().BeTrue();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Errors.Should().OnlyContain(e => e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Valid_Reaches_Handler()
    {
        var called = false;
        var behavior = CreateBehavior(new ProbeValidator());

        RequestHandlerDelegate<ErrorOr<string>> next = _ =>
        {
            called = true;
            return ValueTask.FromResult<ErrorOr<string>>("ok");
        };

        var result = await behavior.Handle(new ProbeRequest("abc"), next, CancellationToken.None);

        called.Should().BeTrue();
        result.IsError.Should().BeFalse();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task NoValidators_Passthrough()
    {
        var called = false;
        var behavior = CreateBehavior();

        RequestHandlerDelegate<ErrorOr<string>> next = _ =>
        {
            called = true;
            return ValueTask.FromResult<ErrorOr<string>>("passthrough");
        };

        var result = await behavior.Handle(new ProbeRequest(""), next, CancellationToken.None);

        called.Should().BeTrue();
        result.Value.Should().Be("passthrough");
    }

    private static ValidationBehavior<ProbeRequest, ErrorOr<string>> CreateBehavior(
        params IValidator<ProbeRequest>[] validators)
        => new(validators, NullLogger<ValidationBehavior<ProbeRequest, ErrorOr<string>>>.Instance);
}
