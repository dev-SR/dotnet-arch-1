using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.RegisterUser;

public sealed record RegisterUserCommand(string Email, string Password) : ICommand;
