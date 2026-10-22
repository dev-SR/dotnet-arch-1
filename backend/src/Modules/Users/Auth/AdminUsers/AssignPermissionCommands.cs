using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.AdminUsers;

public sealed record AssignPermissionCommand(Guid RoleId, string Permission) : ICommand;

public sealed record RemovePermissionCommand(Guid RoleId, string Permission) : ICommand;
