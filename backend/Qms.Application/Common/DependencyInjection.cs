using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Qms.Application.Common;

public static class DependencyInjection
{
    /// <summary>Registers every handler and validator of the Application assembly (ADR-002).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddHandlersAndValidators(typeof(DependencyInjection).Assembly);

    /// <summary>
    /// Scans <paramref name="assembly"/> for <see cref="ICommandHandler{TCommand, TResult}"/> /
    /// <see cref="IQueryHandler{TQuery, TResult}"/> implementations and FluentValidation validators.
    /// Each handler is registered behind its validation decorator, so callers inject only the handler interface.
    /// </summary>
    public static IServiceCollection AddHandlersAndValidators(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces().Where(IsHandlerInterface))
            {
                RegisterDecorated(services, type, handlerInterface);
            }
        }

        return services;
    }

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
         type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));

    private static void RegisterDecorated(IServiceCollection services, Type implementation, Type handlerInterface)
    {
        var arguments = handlerInterface.GetGenericArguments();
        var decoratorType = handlerInterface.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            ? typeof(ValidationDecorator<,>).MakeGenericType(arguments)
            : typeof(QueryValidationDecorator<,>).MakeGenericType(arguments);

        // The concrete handler is registered as itself; the interface resolves to the decorator wrapping it.
        services.AddScoped(implementation);
        services.AddScoped(
            handlerInterface,
            provider => ActivatorUtilities.CreateInstance(provider, decoratorType, provider.GetRequiredService(implementation)));
    }
}
