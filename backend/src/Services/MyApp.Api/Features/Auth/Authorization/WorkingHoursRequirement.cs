using Microsoft.AspNetCore.Authorization;

namespace MyApp.Features.Auth.Authorization;

public sealed class WorkingHoursRequirement : IAuthorizationRequirement
{
    public int StartHour { get; }
    public int EndHour { get; }

    public WorkingHoursRequirement(int startHour, int endHour)
    {
        StartHour = startHour;
        EndHour = endHour;
    }
}

public sealed class WorkingHoursHandler : AuthorizationHandler<WorkingHoursRequirement>
{
    private readonly TimeProvider _clock;

    public WorkingHoursHandler(TimeProvider clock) => _clock = clock;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkingHoursRequirement requirement)
    {
        // Demo override: X-Demo-Hour: 0-23 bypasses wall clock (tests / API demos).
        var http = context.Resource as HttpContext;
        if (http?.Request.Headers.TryGetValue("X-Demo-Hour", out var demo) == true
            && int.TryParse(demo.ToString(), out var hour)
            && hour is >= 0 and <= 23)
        {
            if (hour >= requirement.StartHour && hour < requirement.EndHour)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var nowHour = _clock.GetUtcNow().Hour;
        if (nowHour >= requirement.StartHour && nowHour < requirement.EndHour)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
