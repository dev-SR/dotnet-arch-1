using MyApp.InvoicesApi.Application;
using MyApp.InvoicesApi.Infrastructure;
using MyApp.InvoicesApi.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.UseInvoicesApiMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
