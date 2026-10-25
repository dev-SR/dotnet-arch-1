using System.Security.Claims;
using System.Text.Json.Serialization;
using Shared.Application.Abstractions.Commands;

namespace MyApp.Modules.Users.Auth.AuthDemo;

public sealed record MfaTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record IssueMfaTokenCommand(ClaimsPrincipal Principal) : ICommand<MfaTokenResponse>;

