using MyApp.OrdersApi.Application;
using MyApp.OrdersApi.Infrastructure;
using MyApp.OrdersApi.Presentation;
using MyApp.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(builder.Configuration, "myapp.ordersapi")
    .AddApi(builder.Configuration);

builder.UseOrdersApiMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
