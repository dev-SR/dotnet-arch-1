using Mediator;
using Shared.Common.Errors;

namespace Shared.Application.Abstractions.Commands;

/// <summary>Marks a Mediator request as a command (eligible for TransactionBehavior).</summary>
public interface ICommandMarker;

/// <summary>
/// Opt-in: TransactionBehavior commits even when the handler returns an ErrorOr failure
/// (e.g. lockout counters, refresh-token family revoke on reuse).
/// </summary>
public interface IPersistOnFailure;

public interface ICommand<TResponse> : IRequest<ErrorOr<TResponse>>, ICommandMarker { }
public interface ICommand : IRequest<ErrorOr<Success>>, ICommandMarker { }
