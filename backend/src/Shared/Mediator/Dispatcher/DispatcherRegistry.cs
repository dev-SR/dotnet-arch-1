// Dispatcher/DispatcherRegistry.cs

using System.Collections.Frozen;

namespace Mediator;

internal sealed class DispatcherRegistry(
    FrozenDictionary<Type, RequestHandlerBase> requestWrappers)
{
    public FrozenDictionary<Type, RequestHandlerBase> RequestWrappers { get; } = requestWrappers;
}
