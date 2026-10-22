using Shared.Application.Abstractions.Commands;
using MyApp.Modules.Users.Auth;

namespace MyApp.Modules.Users.Auth.Login;

public sealed record LoginCommand(string Email, string Password)
    : ICommand<TokenResponse>, IPersistOnFailure;
