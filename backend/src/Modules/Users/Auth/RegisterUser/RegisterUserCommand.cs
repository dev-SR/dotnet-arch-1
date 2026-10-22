using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.RegisterUser;

public sealed record RegisterUserCommand(string Email, string Password) : ICommand;
