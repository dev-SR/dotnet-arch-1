using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.AdminUsers;

public sealed record BanUserCommand(Guid UserId, Guid CallerId) : ICommand;

public sealed record UnbanUserCommand(Guid UserId) : ICommand;
