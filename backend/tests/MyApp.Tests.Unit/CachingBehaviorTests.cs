using FluentAssertions;
using Mediator;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Behaviors;
using Shared.Common.Errors;

namespace MyApp.Tests.Unit;

public class CachingBehaviorTests
{
    private sealed record ProbeQuery(string Key, bool Fail) : IRequest<ErrorOr<int>>, ICacheable
    {
        public string CacheKey => $"probe:{Key}:{Fail}";
    }

    [Fact]
    public async Task Success_IsCached_HandlerRunsOnce()
    {
        var behavior = CreateBehavior();
        var calls = 0;
        RequestHandlerDelegate<ErrorOr<int>> next = _ =>
        {
            calls++;
            return ValueTask.FromResult<ErrorOr<int>>(42);
        };

        var first = await behavior.Handle(new ProbeQuery("ok", false), next, CancellationToken.None);
        var second = await behavior.Handle(new ProbeQuery("ok", false), next, CancellationToken.None);

        calls.Should().Be(1);
        first.Value.Should().Be(42);
        second.Value.Should().Be(42);
    }

    [Fact]
    public async Task Failure_IsNotCached_HandlerRunsEveryTime()
    {
        var behavior = CreateBehavior();
        var calls = 0;
        RequestHandlerDelegate<ErrorOr<int>> next = _ =>
        {
            calls++;
            return ValueTask.FromResult<ErrorOr<int>>(Error.NotFound("PROBE.MISSING", "missing"));
        };

        var first = await behavior.Handle(new ProbeQuery("fail", true), next, CancellationToken.None);
        var second = await behavior.Handle(new ProbeQuery("fail", true), next, CancellationToken.None);

        calls.Should().Be(2);
        first.IsError.Should().BeTrue();
        first.FirstError.Code.Should().Be("PROBE.MISSING");
        second.IsError.Should().BeTrue();
    }

    private static CachingBehavior<ProbeQuery, ErrorOr<int>> CreateBehavior()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return new CachingBehavior<ProbeQuery, ErrorOr<int>>(
            cache,
            NullLogger<CachingBehavior<ProbeQuery, ErrorOr<int>>>.Instance);
    }
}
