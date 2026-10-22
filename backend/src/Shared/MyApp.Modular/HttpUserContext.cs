using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace MyApp.Modular;

public sealed class HttpUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public const string SystemUserId = "system";

    public string UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return SystemUserId;

            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub")
                   ?? SystemUserId;
        }
    }
}
