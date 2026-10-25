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

    options.AddDocument("auth", "Auth + Products", routePattern: "/openapi/auth/v1.json", isDefault: true);
    options.AddDocument("orders", "Orders + Cart", routePattern: "/openapi/orders/v1.json");
    options.AddDocument("invoices", "Invoices", routePattern: "/openapi/invoices/v1.json");
    options.AddDocument("shipping", "Shipping", routePattern: "/openapi/shipping/v1.json");
});

app.MapReverseProxy();

app.Run();
