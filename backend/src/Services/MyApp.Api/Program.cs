using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Presentation;

Env.TraversePath().Load(); // before CreateBuilder so Jwt__* bind into IConfiguration

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseInfrastructure()
    .UsePresentation();

app.Run();

public partial class Program;
