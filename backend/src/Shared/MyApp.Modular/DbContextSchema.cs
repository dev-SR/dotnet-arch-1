using Microsoft.EntityFrameworkCore;

namespace MyApp.Modular;

/// <summary>
/// Shared migrate / EnsureCreated bootstrap used by every module's <c>UseXxxModule</c>.
/// </summary>
public static class DbContextSchema
{
    public static void Apply(DbContext db)
    {
        if (!db.Database.IsRelational())
        {
            db.Database.EnsureCreated();
            return;
        }

        if (db.Database.GetMigrations().Any())
            db.Database.Migrate();
        else
            db.Database.EnsureCreated();
    }
}
