namespace MyApp.Modules.Users.Auth.AdminUsers;

public sealed class AdminPermissionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/admin/roles/{roleId:guid}/permissions")
            .WithTags("AuthAdmin")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapPost("/", AssignAsync).WithName("AssignRolePermission");
        group.MapDelete("/", RemoveAsync).WithName("RemoveRolePermission");
        group.MapGet("/", ListAsync).WithName("ListRolePermissions");
    }

    private static async Task<IResult> AssignAsync(
        Guid roleId,
        AssignPermissionBody body,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new AssignPermissionCommand(roleId, body.Permission), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> RemoveAsync(
        Guid roleId,
        string permission,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RemovePermissionCommand(roleId, permission), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> ListAsync(
        Guid roleId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListRolePermissionsQuery(roleId), ct);
        return result.MatchOk(http);
    }

    public sealed record AssignPermissionBody(string Permission);
}
