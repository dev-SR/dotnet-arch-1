using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.RegisterUser;

internal sealed class RegisterUserCommandHandler(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher)
    : IRequestHandler<RegisterUserCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        var normalized = command.Email.Trim().ToUpperInvariant();
        if (await db.Users
                .Apply(new UserByNormalizedEmailSpecification(normalized))
                .AnyAsync(ct))
            return Error.Conflict("AUTH.EMAIL_TAKEN", "Email is already taken.");

        var userRole = await db.Roles
            .Apply(new RoleByNormalizedNameSpecification(Roles.User.ToUpperInvariant()))
            .SingleAsync(ct);

        var user = new User
        {
            Email = command.Email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = "",
        };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);

        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = userRole.Id });
        // SaveChanges via TransactionBehavior
        return default(Success);
    }
}
