using Shared.Application.Abstractions.Commands;
using MyApp.Features.Auth;

namespace MyApp.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password)
    : ICommand<TokenResponse>, IPersistOnFailure;
