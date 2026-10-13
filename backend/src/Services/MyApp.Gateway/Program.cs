var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));

// Pure YARP — does not validate JWT; forwards Authorization to AuthApi / ProductsApi.
app.MapReverseProxy();

app.Run();
