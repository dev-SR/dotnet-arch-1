using Mediator;
using Shared.Common.Errors;

namespace Shared.Application.Abstractions.Queries;

public interface IQuery<TResponse> : IRequest<ErrorOr<TResponse>> { }
