namespace Mediator;

// Marker for any request that returns a response
public interface IRequest<out TResponse>;
// Void-equivalent for commands with no return value
public interface IRequest : IRequest<Unit>;
public readonly struct Unit
{
    public static readonly Unit Value = default;
}
