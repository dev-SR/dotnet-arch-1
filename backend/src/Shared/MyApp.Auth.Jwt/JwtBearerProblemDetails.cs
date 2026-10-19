using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Shared.Common.Errors;
using Shared.Common.Errors.Http;

namespace MyApp.Auth.Jwt;

/// <summary>
/// Shared JWT challenge / forbid → RFC 9457 Problem Details (same shape as ErrorOr → ToProblem).
/// </summary>
public static class JwtBearerProblemDetails
{
    public const string ChallengeErrorItemKey = "auth.challengeError";

    public static async Task WriteAsync(HttpContext http, Error error)
    {
        if (http.Response.HasStarted) return;

        http.Response.Headers.WWWAuthenticate = error.Code switch
        {
            "AUTH.TOKEN_EXPIRED" => "Bearer error=\"invalid_token\", error_description=\"The access token has expired\"",
            "AUTH.TOKEN_INVALID" or "AUTH.INVALID_TOKEN" => "Bearer error=\"invalid_token\"",
            _ => "Bearer",
        };

        await error.ToProblem(http).ExecuteAsync(http);
    }

    public static async Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.HttpContext.Response.HasStarted) return;

        var error = context.HttpContext.Items.TryGetValue(ChallengeErrorItemKey, out var boxed)
                    && boxed is Error stored
            ? stored
            : MapChallengeError(context);
        await WriteAsync(context.HttpContext, error);
    }

    public static async Task OnForbiddenAsync(ForbiddenContext context)
    {
        if (context.HttpContext.Response.HasStarted) return;
        await WriteAsync(
            context.HttpContext,
            Error.Forbidden("AUTH.FORBIDDEN", "You are not allowed to perform this action."));
    }

    public static Error MapChallengeError(JwtBearerChallengeContext context) =>
        context.AuthenticateFailure is not null
            ? Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid or expired access token.")
            : Error.Unauthorized("AUTH.UNAUTHENTICATED", "Authentication is required.");
}
