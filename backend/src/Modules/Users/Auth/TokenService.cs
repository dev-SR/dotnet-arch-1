using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Auth.Jwt;

namespace MyApp.Modules.Users.Auth;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _creds;

    public TokenService(IOptions<JwtOptions> jwt, TimeProvider clock)
    {
        _jwt = jwt.Value;
        _clock = clock;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        _creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string CreateAccessToken(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now.UtcDateTime).ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(AuthClaims.TokenVersion, user.PermissionVersion.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(AuthClaims.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(AuthClaims.Permission, p)));

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
        var raw = Base64UrlEncoder.Encode(bytes);
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
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        Guid familyId)
    {
        var accessToken = CreateAccessToken(user, roles, permissions);
        var (rawRefreshToken, hash, expiresAt) = GenerateRefreshToken();
        var now = _clock.GetUtcNow().UtcDateTime;

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            FamilyId = familyId,
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
