namespace MyApp.Modules.Users;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? UserEmail { get; set; }
    public string? UserPassword { get; set; }
}
