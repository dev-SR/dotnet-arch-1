using System.Net.Sockets;

namespace MyApp.Tests.Integration;

/// <summary>
/// Waits for RabbitMQ from <c>docker compose</c> (<c>amqp://guest:guest@localhost:5672</c>).
/// Same model as <see cref="ElasticsearchFixture"/> — no Testcontainers / docker.sock required by the test process.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = "amqp://guest:guest@localhost:5672";

    public async Task InitializeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", 5672);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(1000);
        }

        throw new InvalidOperationException(
            "RabbitMQ is not reachable at localhost:5672. Run: docker compose up -d",
            last);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
