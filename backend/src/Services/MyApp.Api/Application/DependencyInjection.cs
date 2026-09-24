using System.Reflection;
using FluentValidation;
using MyApp.Persistence;
using Shared.Application.Behaviors;

namespace MyApp.Application;
// The Application layer registers MediatR, FluentValidation, and application services. It knows nothing about HTTP, databases, or external APIs.
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {

        // Mediator - discovers all handlers in this assembly
        services.RegisterRequestHandlers(Assembly.GetExecutingAssembly());
        services.AddHybridCache();
        // Behaviors compose in registration order
        // LoggingBehavior is outermost - sees request first, response last
        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(CachingBehavior<,>));
        services.AddPipelineBehavior(typeof(TransactionBehavior<,>));

        // FluentValidation - discovers all validators
        services.AddValidatorsFromAssembly(
            typeof(ApplicationServiceCollectionExtensions).Assembly,
            includeInternalTypes: true);
        // Application services (stateless, often transient)
        // services.AddTransient<IDateTimeProvider, DateTimeProvider>();
        // services.AddSingleton<IEmailTemplateEngine, EmailTemplateEngine>();
        return services;
    }
}
