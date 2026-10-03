using Shared.Application.Abstractions.Commands;
using MyApp.Features.Auth;

namespace MyApp.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken)
    : ICommand<TokenResponse>, IPersistOnFailure;
