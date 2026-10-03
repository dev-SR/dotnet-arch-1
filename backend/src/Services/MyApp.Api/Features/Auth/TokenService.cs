using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Config;

namespace MyApp.Features.Auth;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _creds;

    public TokenService(IOptions<JwtOptions> jwt, TimeProvider clock)
    {
        _jwt = jwt.Value;
        _clock = clock;
        // Cache key + credentials once — do not rebuild per request.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        _creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), //~sub = userId
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now.UtcDateTime).ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(AuthClaims.TokenVersion, user.PermissionVersion.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(AuthClaims.Role, r)));
        //~new Claim("role", "X"), new Claim("role", "Y")....


        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _creds,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public (string Raw, string Hash, DateTime ExpiresAt) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var raw = Base64UrlEncoder.Encode(bytes); // URL-safe; avoid + / =
        var expires = _clock.GetUtcNow().UtcDateTime.AddDays(_jwt.RefreshTokenDays);
        return (raw, Hash(raw), expires);
    }

    public string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public int AccessTokenExpiresInSeconds => _jwt.AccessTokenMinutes * 60;

    public IssuedTokens IssueTokens(
        User user, IEnumerable<string> roles, Guid familyId)
    {
        var accessToken = CreateAccessToken(user, roles);
        var (rawRefreshToken, hash, expiresAt) = GenerateRefreshToken();
        var now = _clock.GetUtcNow().UtcDateTime;

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,       // store hash only — never the raw token
            FamilyId = familyId,    // same family across rotations; reuse kills the family
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        return new IssuedTokens(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            refreshToken);
    }
}
