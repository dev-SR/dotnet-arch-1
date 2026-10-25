using FluentValidation;
using MyApp.Modular;
using Shared.Application.Behaviors;

namespace MyApp.InvoicesApi.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;

        services.RegisterRequestHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(TransactionBehavior<,>));

        return services;
    }
}
