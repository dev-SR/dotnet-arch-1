using Shared.Application.Abstractions.Commands;
using Shared.Application.Abstractions.Queries;
using MyApp.Features.Auth.AdminRoles;

namespace MyApp.Features.Auth.AdminUsers;

public sealed record ListUserRolesQuery(Guid UserId) : IQuery<IReadOnlyList<RoleListItem>>;

public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand;

public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand;
