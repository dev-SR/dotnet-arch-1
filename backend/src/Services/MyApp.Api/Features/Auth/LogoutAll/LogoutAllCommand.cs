using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId) : ICommand;
