using Shared.Application.Abstractions.Commands;
using Shared.Application.Abstractions.Queries;

namespace MyApp.Features.Auth.AdminRoles;

public sealed record RoleListItem(Guid Id, string Name, string NormalizedName);

public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleListItem>>;

public sealed record CreateRoleCommand(string Name) : ICommand<RoleListItem>;

public sealed record RenameRoleCommand(Guid RoleId, string Name) : ICommand;

public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;
