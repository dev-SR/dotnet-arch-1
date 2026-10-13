using System.Collections.Concurrent;
using MyApp.Auth.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Hospital-style one-liner — same shared secret as AuthApi; claim-only permissions.
builder.Services.AddMyAppAuth(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "products" }))
    .AllowAnonymous();

var store = new ConcurrentDictionary<Guid, ProductDto>();

app.MapGet("/products", () => Results.Ok(store.Values.OrderBy(p => p.Name)))
    .AllowAnonymous();

app.MapGet("/products/{id:guid}", (Guid id) =>
        store.TryGetValue(id, out var p) ? Results.Ok(p) : Results.NotFound())
    .AllowAnonymous();

app.MapPost("/products", (CreateProductBody body) =>
    {
        var product = new ProductDto(Guid.NewGuid(), body.Name.Trim(), body.Category.Trim(), body.Price, body.Stock);
        store[product.Id] = product;
        return Results.Created($"/products/{product.Id}", product);
    })
    .RequireAuthorization(Permissions.Products.Create);

app.MapPut("/products/{id:guid}", (Guid id, CreateProductBody body) =>
    {
        if (!store.ContainsKey(id))
            return Results.NotFound();
        var product = new ProductDto(id, body.Name.Trim(), body.Category.Trim(), body.Price, body.Stock);
        store[id] = product;
        return Results.NoContent();
    })
    .RequireAuthorization(Permissions.Products.Edit);

app.MapDelete("/products/{id:guid}", (Guid id) =>
        store.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound())
    .RequireAuthorization(Permissions.Products.Delete);

app.Run();

internal sealed record ProductDto(Guid Id, string Name, string Category, decimal Price, int Stock);
internal sealed record CreateProductBody(string Name, string Category, decimal Price, int Stock);
