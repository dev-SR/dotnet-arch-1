using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId) : ICommand;
