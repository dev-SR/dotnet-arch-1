using Shared.Application.Abstractions.Commands;
using MyApp.Modules.Users.Auth;

namespace MyApp.Modules.Users.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken)
    : ICommand<TokenResponse>, IPersistOnFailure;
