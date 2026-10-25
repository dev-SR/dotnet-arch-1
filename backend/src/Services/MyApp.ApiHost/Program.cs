using Carter;
using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Modular;
using MyApp.Modules.Catalog;
using MyApp.Modules.Search;
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
    .AddCatalogModule(builder.Configuration)
    .AddSearchModule(builder.Configuration);

builder.UseApiHostMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.UseUsersModule();
app.UseCatalogModule();
app.UseSearchModule();
app.UseDevelopmentSeeders();
app.MapCarter();

app.Run();

public partial class Program;
