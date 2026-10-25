using MyApp.OrdersApi.Application;
using MyApp.OrdersApi.Infrastructure;
using MyApp.OrdersApi.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.UseOrdersApiMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
