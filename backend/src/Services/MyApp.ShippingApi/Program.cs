using MyApp.ShippingApi.Application;
using MyApp.ShippingApi.Infrastructure;
using MyApp.ShippingApi.Presentation;
using MyApp.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(builder.Configuration, "myapp.shippingapi")
    .AddApi(builder.Configuration);

builder.UseShippingApiMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
