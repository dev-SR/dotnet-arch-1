using Shared.Application.Abstractions.Queries;

namespace MyApp.Modules.Users.Auth.AdminUsers;

public sealed record ListRolePermissionsQuery(Guid RoleId) : IQuery<IReadOnlyList<string>>;
