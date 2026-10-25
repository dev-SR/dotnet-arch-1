namespace MyApp.Tests.Integration;

/// <summary>
/// Waits for Elasticsearch from <c>docker compose</c> (<c>http://localhost:9200</c>).
/// Prefer compose over Testcontainers here — the official ES image is heavy and already in compose.
/// </summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    public string Uri { get; } = "http://localhost:9200";

    public async Task InitializeAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(120);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(Uri);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(1000);
        }

        throw new InvalidOperationException(
            "Elasticsearch is not reachable at http://localhost:9200. Run: docker compose up -d",
            last);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
