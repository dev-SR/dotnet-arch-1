using MyApp.Gateway;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddOpenApiProxyClients(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));

app.MapOpenApiProxyEndpoints();
app.MapScalarApiReference("/scalar", options =>
{
    options.WithTitle("MyApp Gateway")
        .EnablePersistentAuthentication()
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);

    // Separate OpenAPI docs — pick "Invoices" in the top-left document dropdown.
    options.AddDocument(
        "auth",
        "Auth + Products",
        routePattern: "/openapi/auth/v1.json",
        isDefault: true);

    options.AddDocument(
        "invoices",
        "Invoices",
        routePattern: "/openapi/invoices/v1.json");
});

app.MapReverseProxy();

app.Run();
