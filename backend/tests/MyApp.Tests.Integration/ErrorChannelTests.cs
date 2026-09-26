using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.Persistence;

namespace MyApp.Tests.Integration;

public class ErrorChannelTests(MyAppFactory factory) : IClassFixture<MyAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Product feature path ──────────────────────────────────────────────

    [Fact]
    public async Task CreateProduct_Invalid_ReturnsValidationProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/products", new
        {
            Name = "ab",
            Category = "x",
            Price = 0,
            Stock = -1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("VALIDATION");
        problem.GetProperty("errorType").GetString().Should().Be("Validation");
        problem.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetProduct_Missing_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync($"/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("PRODUCT.NOT_FOUND");
        problem.GetProperty("errorType").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateAndGetProduct_Succeeds()
    {
        var create = await _client.PostAsJsonAsync("/products", new
        {
            Name = "Keyboard",
            Category = "Hardware",
            Price = 99.5m,
            Stock = 3,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        id.Should().NotBeEmpty();

        var get = await _client.GetAsync($"/products/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateProduct_Returns204_OnSuccess()
    {
        var id = await CreateProductAsync("Mouse", "Hardware", 25m, 10);

        var update = await _client.PutAsJsonAsync($"/products/{id}", new
        {
            Name = "Gaming Mouse",
            Category = "Hardware",
            Price = 49.9m,
            Stock = 5,
        });

        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/products/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await get.Content.ReadAsStringAsync();
        json.Should().Contain("Gaming Mouse");
    }

    [Fact]
    public async Task UpdateProduct_Missing_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/products/{Guid.NewGuid()}", new
        {
            Name = "Ghost",
            Category = "Nowhere",
            Price = 1m,
            Stock = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemAsync(response)).GetProperty("errorCode").GetString()
            .Should().Be("PRODUCT.NOT_FOUND");
    }

    [Fact]
    public async Task UpdateProduct_Invalid_ReturnsValidation400()
    {
        var id = await CreateProductAsync("ValidName", "Category", 10m, 1);

        var response = await _client.PutAsJsonAsync($"/products/{id}", new
        {
            Name = "ab",
            Category = "x",
            Price = 0,
            Stock = -1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION");
    }

    [Fact]
    public async Task DeleteProduct_Returns204_ThenGetIs404()
    {
        var id = await CreateProductAsync("Temp", "Misc", 5m, 1);

        var delete = await _client.DeleteAsync($"/products/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/products/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_Missing_Returns404()
    {
        var response = await _client.DeleteAsync($"/products/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemAsync(response)).GetProperty("errorCode").GetString()
            .Should().Be("PRODUCT.NOT_FOUND");
    }

    // ── Diag result / throw matrix ────────────────────────────────────────

    [Theory]
    [InlineData("ok", HttpStatusCode.OK)]
    [InlineData("validation", HttpStatusCode.BadRequest)]
    [InlineData("validation-multi", HttpStatusCode.BadRequest)]
    [InlineData("mixed", HttpStatusCode.Forbidden)]
    [InlineData("not-found", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("failure", HttpStatusCode.BadRequest)]
    [InlineData("unexpected", HttpStatusCode.InternalServerError)]
    public async Task Diag_Result_Returns_Expected_Status(string kind, HttpStatusCode expected)
    {
        var response = await _client.GetAsync($"/_diag/result/{kind}");
        response.StatusCode.Should().Be(expected);

        if (expected == HttpStatusCode.OK)
            return;

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("unexpected", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")]
    [InlineData("argument", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")]
    public async Task Diag_Throw_Returns_Expected_Status(
        string kind, HttpStatusCode expected, string errorCode)
    {
        var response = await _client.GetAsync($"/_diag/throw/{kind}");
        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be(errorCode);
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Diag_ValidationMulti_Groups_Fields()
    {
        var response = await _client.GetAsync("/_diag/result/validation-multi");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("VALIDATION");
        var errors = problem.GetProperty("errors");
        errors.GetProperty("FirstName").GetArrayLength().Should().Be(2);
        errors.GetProperty("Email").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Diag_Mixed_Prefers_Forbidden_Over_Validation()
    {
        var response = await _client.GetAsync("/_diag/result/mixed");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("DIAG.FORBIDDEN");
        problem.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Diag_Unexpected_Result_Does_Not_Leak_Secret()
    {
        var response = await _client.GetAsync("/_diag/result/unexpected");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("secret internal detail");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task Diag_Unexpected_Throw_Does_Not_Leak_Secret()
    {
        var response = await _client.GetAsync("/_diag/throw/unexpected");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("secret internal detail");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
    }

    // ── Framework / binding edges ─────────────────────────────────────────

    [Fact]
    public async Task Diag_WrongMethod_Returns405_ProblemBody()
    {
        var response = await _client.PostAsync("/_diag/result/ok", null);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Diag_Body_MalformedJson_ReturnsBadRequest()
    {
        using var content = new StringContent("{ not-json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/_diag/body", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().BeOneOf(
            "application/problem+json",
            "application/json");
    }

    [Fact]
    public async Task Diag_Body_WrongContentType_ReturnsProblem()
    {
        using var content = new StringContent("Name=x&Age=1", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/_diag/body", content);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.UnsupportedMediaType, HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateProduct_MalformedJson_ReturnsBadRequest()
    {
        using var content = new StringContent("{ not-json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/products", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().BeOneOf(
            "application/problem+json",
            "application/json");
        var problem = await ReadProblemAsync(response);
        if (problem.TryGetProperty("errorCode", out var code))
            code.GetString().Should().Be("BAD_REQUEST");
        else
            problem.TryGetProperty("title", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetailsViaStatusCodePages()
    {
        var response = await _client.GetAsync("/this-route-does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Production_Hides_Diag_Routes()
    {
        await using var productionFactory = new ProductionMyAppFactory();
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/_diag/result/ok");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateProductAsync(string name, string category, decimal price, int stock)
    {
        var create = await _client.PostAsJsonAsync("/products", new
        {
            Name = name,
            Category = category,
            Price = price,
            Stock = stock,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}

/// <summary>Same as <see cref="MyAppFactory"/> but Production — diag routes must not register.</summary>
file sealed class ProductionMyAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"myapp-prod-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }
}
