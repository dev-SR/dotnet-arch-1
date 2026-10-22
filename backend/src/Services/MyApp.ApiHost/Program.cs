using Carter;
using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Messaging;
using MyApp.Modular;
using MyApp.Modules.Catalog;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Orders;
using MyApp.Modules.Orders.Catalog;
using MyApp.Modules.Orders.Persistence;
using MyApp.Modules.Users;
using MyApp.Presentation;
using Wolverine.Persistence.Durability;

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

builder.UseTransactionalMessaging(msg =>
{
    msg.AddStore<CatalogDbContext>(
        TransactionalMessagingOptions.RequireConnectionString(builder.Configuration, "Catalog"),
        MessageStoreRole.Main);
    msg.AddStore<OrdersDbContext>(
        TransactionalMessagingOptions.RequireConnectionString(builder.Configuration, "Orders"),
        MessageStoreRole.Ancillary);
    // Explicit type only — IncludeAssembly would also pick Mediator handlers named Handle.
    msg.IncludeHandler<ProductCreatedIntegrationEventHandler>();
});

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
