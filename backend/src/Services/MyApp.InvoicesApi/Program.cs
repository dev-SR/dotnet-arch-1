using MyApp.InvoicesApi.Application;
using MyApp.InvoicesApi.Infrastructure;
using MyApp.InvoicesApi.Presentation;
using MyApp.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(builder.Configuration, "myapp.invoicesapi")
    .AddApi(builder.Configuration);

builder.UseInvoicesApiMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
