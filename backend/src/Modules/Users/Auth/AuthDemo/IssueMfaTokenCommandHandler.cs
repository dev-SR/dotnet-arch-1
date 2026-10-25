using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Auth.Jwt;
using Shared.Common.Errors;

namespace MyApp.Modules.Users.Auth.AuthDemo;

internal sealed class IssueMfaTokenCommandHandler(
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock)
    : IRequestHandler<IssueMfaTokenCommand, ErrorOr<MfaTokenResponse>>
{
    public ValueTask<ErrorOr<MfaTokenResponse>> Handle(
        IssueMfaTokenCommand command,
        CancellationToken cancellationToken)
    {
        var jwt = jwtOptions.Value;
        var now = clock.GetUtcNow();
        var claims = command.Principal.Claims
            .Where(c => c.Type is not (JwtRegisteredClaimNames.Iat or JwtRegisteredClaimNames.Nbf
                or JwtRegisteredClaimNames.Exp or JwtRegisteredClaimNames.Jti))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();

        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));
        claims.Add(new Claim(JwtRegisteredClaimNames.Iat,
            EpochTime.GetIntDate(now.UtcDateTime).ToString(), ClaimValueTypes.Integer64));
        if (!claims.Any(c => c.Type == AuthClaims.Amr && c.Value == "mfa"))
            claims.Add(new Claim(AuthClaims.Amr, "mfa"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.AddMinutes(jwt.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return ValueTask.FromResult<ErrorOr<MfaTokenResponse>>(
            new MfaTokenResponse(token, "Bearer", jwt.AccessTokenMinutes * 60));
    }
}
