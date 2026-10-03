using Shared.Common.Errors;
using Shared.Common.Errors.Http;

namespace MyApp.Features.Auth;

internal static class JwtBearerProblem
{
    public const string ChallengeErrorItemKey = "auth.challengeError";

    public static async Task WriteAsync(HttpContext http, Error error)
    {
        if (http.Response.HasStarted) return;

        http.Response.Headers.WWWAuthenticate = error.Code switch
        {
            "AUTH.TOKEN_EXPIRED" => "Bearer error=\"invalid_token\", error_description=\"The access token has expired\"",
            "AUTH.TOKEN_INVALID" => "Bearer error=\"invalid_token\"",
            _ => "Bearer",
        };

        await error.ToProblem(http).ExecuteAsync(http);
    }
}
