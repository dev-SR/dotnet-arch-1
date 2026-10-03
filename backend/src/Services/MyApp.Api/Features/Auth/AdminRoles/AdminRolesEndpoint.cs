namespace MyApp.Features.Auth.AdminRoles;

public sealed class AdminRolesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/admin/roles")
            .WithTags("AuthAdmin")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/", ListAsync).WithName("ListRoles");
        group.MapPost("/", CreateAsync).WithName("CreateRole");
        group.MapPut("/{roleId:guid}", RenameAsync).WithName("RenameRole");
        group.MapDelete("/{roleId:guid}", DeleteAsync).WithName("DeleteRole");
    }

    private static async Task<IResult> ListAsync(
        IMediator mediator, HttpContext http, CancellationToken ct)
    {
        var result = await mediator.Send(new ListRolesQuery(), ct);
        return result.MatchOk(http);
    }

    private static async Task<IResult> CreateAsync(
        CreateRoleBody body,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateRoleCommand(body.Name), ct);
        return result.MatchCreated(http, r => $"/auth/admin/roles/{r.Id}");
    }

    private static async Task<IResult> RenameAsync(
        Guid roleId,
        RenameRoleBody body,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RenameRoleCommand(roleId, body.Name), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> DeleteAsync(
        Guid roleId,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteRoleCommand(roleId), ct);
        return result.MatchNoContent(http);
    }

    public sealed record CreateRoleBody(string Name);
    public sealed record RenameRoleBody(string Name);
}
