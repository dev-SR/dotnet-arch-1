using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace MyApp.Config;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32)] public string Secret { get; set; } = "";
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 10;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 7;
}

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    [Range(1, 10_000)] public int GlobalPerMinute { get; set; } = 120;
    [Range(1, 100)] public int AuthPerMinute { get; set; } = 5;
}

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }

    public string? UserEmail { get; set; }
    public string? UserPassword { get; set; }
}

public static class OptionsExtensions
{
    /// Bind + validate + fail fast at startup (a weak or missing secret should crash the app, not ship).
    public static OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, string section)
        where T : class =>
        services.AddOptions<T>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
