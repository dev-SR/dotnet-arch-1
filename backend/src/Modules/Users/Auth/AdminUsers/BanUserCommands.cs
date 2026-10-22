using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.AdminUsers;

public sealed record BanUserCommand(Guid UserId, Guid CallerId) : ICommand;

public sealed record UnbanUserCommand(Guid UserId) : ICommand;
