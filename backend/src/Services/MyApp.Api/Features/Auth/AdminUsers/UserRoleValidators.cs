using FluentValidation;

namespace MyApp.Features.Auth.AdminUsers;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class RemoveRoleCommandValidator : AbstractValidator<RemoveRoleCommand>
{
    public RemoveRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class ListUserRolesQueryValidator : AbstractValidator<ListUserRolesQuery>
{
    public ListUserRolesQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
