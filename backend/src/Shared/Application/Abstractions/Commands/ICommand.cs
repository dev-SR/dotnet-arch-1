using Mediator;
using Shared.Common.Errors;

namespace Shared.Application.Abstractions.Commands;

public interface ICommand<TResponse> : IRequest<ErrorOr<TResponse>> { }
public interface ICommand : IRequest<ErrorOr<Success>> { }   // mutation with no payload to return
