using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;
