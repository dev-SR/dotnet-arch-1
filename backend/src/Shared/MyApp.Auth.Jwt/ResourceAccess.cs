namespace MyApp.Auth.Jwt;

/// <summary>
/// The single rule for permission-plus-ownership checks. Policy handlers and application handlers both call it.
/// </summary>
public static class ResourceAccess
{
    public static bool IsAllowed(Caller caller, Guid ownerId, string permission, bool requireOwnership = true)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!caller.Has(permission))
            return false;

        if (!requireOwnership)
            return true;

        if (caller.IsAdmin)
            return true;

        return caller.UserId != Guid.Empty && caller.UserId == ownerId;
    }
}
