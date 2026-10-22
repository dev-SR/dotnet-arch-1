using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace MyApp.Config;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    [Range(1, 10_000)] public int GlobalPerMinute { get; set; } = 120;
    [Range(1, 100)] public int AuthPerMinute { get; set; } = 5;
}

public static class OptionsExtensions
{
    public static OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, string section)
        where T : class =>
        services.AddOptions<T>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
