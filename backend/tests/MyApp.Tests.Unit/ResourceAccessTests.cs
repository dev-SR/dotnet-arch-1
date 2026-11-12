using FluentAssertions;
using MyApp.Auth.Jwt;

namespace MyApp.Tests.Unit;

public class ResourceAccessTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private const string Edit = "Permissions.Invoices.Edit";

    [Fact]
    public void Owner_with_permission_is_allowed()
    {
        var caller = new Caller(Owner, IsAdmin: false, [Edit]);
        ResourceAccess.IsAllowed(caller, Owner, Edit).Should().BeTrue();
    }

    [Fact]
    public void Non_owner_with_permission_is_denied_when_ownership_is_required()
    {
        var caller = new Caller(Guid.NewGuid(), IsAdmin: false, [Edit]);
        ResourceAccess.IsAllowed(caller, Owner, Edit).Should().BeFalse();
    }

    [Fact]
    public void Non_owner_with_permission_is_allowed_when_ownership_is_not_required()
    {
        var caller = new Caller(Guid.NewGuid(), IsAdmin: false, [Edit]);
        ResourceAccess.IsAllowed(caller, Owner, Edit, requireOwnership: false).Should().BeTrue();
    }

    [Fact]
    public void Admin_bypasses_ownership_but_still_needs_the_permission()
    {
        ResourceAccess.IsAllowed(new Caller(Guid.NewGuid(), IsAdmin: true, [Edit]), Owner, Edit).Should().BeTrue();
        ResourceAccess.IsAllowed(new Caller(Guid.NewGuid(), IsAdmin: true, []), Owner, Edit).Should().BeFalse();
    }

    [Fact]
    public void Missing_permission_is_denied_even_for_the_owner()
    {
        var caller = new Caller(Owner, IsAdmin: false, []);
        ResourceAccess.IsAllowed(caller, Owner, Edit).Should().BeFalse();
    }

    [Fact]
    public void Permission_match_is_case_insensitive()
    {
        var caller = new Caller(Owner, IsAdmin: false, ["permissions.invoices.edit"]);
        ResourceAccess.IsAllowed(caller, Owner, Edit).Should().BeTrue();
    }

    [Fact]
    public void Unauthenticated_caller_with_empty_id_never_matches_an_empty_owner()
    {
        var anonymous = new Caller(Guid.Empty, IsAdmin: false, [Edit]);
        ResourceAccess.IsAllowed(anonymous, Guid.Empty, Edit).Should().BeFalse();
    }
}
