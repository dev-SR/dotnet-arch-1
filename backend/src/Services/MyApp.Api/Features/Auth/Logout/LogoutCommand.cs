using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;
