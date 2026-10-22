using Carter;
using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Modular;
using MyApp.Modules.Catalog;
using MyApp.Modules.Orders;
using MyApp.Modules.Users;
using MyApp.Presentation;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddModularEfCore()
    .AddApi(builder.Configuration)
    .AddUsersModule(builder.Configuration)
    .AddOrdersModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration);

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.UseUsersModule();
app.UseOrdersModule();
app.UseCatalogModule();
app.UseDevelopmentSeeders();
app.MapCarter();

app.Run();

public partial class Program;
