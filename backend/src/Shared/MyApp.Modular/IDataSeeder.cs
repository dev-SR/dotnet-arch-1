namespace MyApp.Modular;

public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
