using System.Security.Claims;

namespace MyApp.Features.Auth.AdminUsers;

public sealed class AdminUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/admin/users")
            .WithTags("AuthAdmin")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/", ListAsync).WithName("ListUsers");
        group.MapPost("/{userId:guid}/ban", BanAsync).WithName("BanUser");
        group.MapPost("/{userId:guid}/unban", UnbanAsync).WithName("UnbanUser");
        group.MapGet("/{userId:guid}/roles", ListRolesAsync).WithName("ListUserRoles");
        group.MapPost("/{userId:guid}/roles/{roleId:guid}", AssignRoleAsync).WithName("AssignUserRole");
        group.MapDelete("/{userId:guid}/roles/{roleId:guid}", RemoveRoleAsync).WithName("RemoveUserRole");
    }

    private static async Task<IResult> ListAsync(
        IMediator mediator, HttpContext http, CancellationToken ct)
    {
        var result = await mediator.Send(new ListUsersQuery(), ct);
        return result.MatchOk(http);
    }

    private static async Task<IResult> BanAsync(
        Guid userId,
        ClaimsPrincipal principal,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new BanUserCommand(userId, principal.GetUserId()), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> UnbanAsync(
        Guid userId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UnbanUserCommand(userId), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> ListRolesAsync(
        Guid userId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListUserRolesQuery(userId), ct);
        return result.MatchOk(http);
    }

    private static async Task<IResult> AssignRoleAsync(
        Guid userId,
        Guid roleId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new AssignRoleCommand(userId, roleId), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> RemoveRoleAsync(
        Guid userId,
        Guid roleId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RemoveRoleCommand(userId, roleId), ct);
        return result.MatchNoContent(http);
    }
}
