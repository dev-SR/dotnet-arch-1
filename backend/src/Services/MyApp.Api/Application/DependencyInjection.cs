using System.Reflection;
using FluentValidation;

namespace MyApp.Application;
// The Application layer registers MediatR, FluentValidation, and application services. It knows nothing about HTTP, databases, or external APIs.
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {

        // Mediator - discovers all handlers in this assembly
        services.AddDispatcher(Assembly.GetExecutingAssembly());

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
