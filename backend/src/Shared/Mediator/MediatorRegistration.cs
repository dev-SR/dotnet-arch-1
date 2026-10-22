// MediatorRegistration.cs

using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mediator;

public static class MediatorRegistration
{
    public static void RegisterRequestHandlers(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var requestWrappers = new Dictionary<Type, RequestHandlerBase>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var genericDef = iface.GetGenericTypeDefinition();

                    if (genericDef == typeof(IRequestHandler<,>))
                    {
                        services.AddScoped(iface, type);
                        var args = iface.GetGenericArguments();
                        var requestType = args[0];
                        var responseType = args[1];
                        if (requestWrappers.ContainsKey(requestType))
                            continue;
                        var wrapperType = typeof(RequestHandlerWrapper<,>)
                            .MakeGenericType(requestType, responseType);
                        requestWrappers[requestType] =
                            (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
                    }
                    else if (genericDef == typeof(INotificationHandler<>))
                    {
                        // Multiple handlers per notification type are allowed.
                        services.AddScoped(iface, type);
                    }
                }
            }
        }

        var registry = new MediatorRegistry(requestWrappers.ToFrozenDictionary());
        services.AddSingleton(registry);
        services.AddScoped<Mediator>();
        services.AddScoped<IMediator>(sp => sp.GetRequiredService<Mediator>());
        services.TryAddSingleton<INotificationPublishStrategy, SequentialNotificationPublishStrategy>();
        services.AddScoped<IPublisher, Publisher>();
    }
}
