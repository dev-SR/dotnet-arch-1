using Shared.Application.Abstractions.Queries;

namespace MyApp.Features.Auth.AdminUsers;

public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserListItem>>;

public sealed record UserListItem(Guid Id, string Email, bool IsBanned, int PermissionVersion);
