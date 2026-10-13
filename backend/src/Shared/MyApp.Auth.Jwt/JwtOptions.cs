using System.ComponentModel.DataAnnotations;

namespace MyApp.Auth.Jwt;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32)] public string Secret { get; set; } = "";
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 10;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 7;
}
