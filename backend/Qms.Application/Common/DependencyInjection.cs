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
    /// <paramref name="typeFilter"/> (optional) limits the scan; the test host uses it to take only its own namespace.
    /// </summary>
    public static IServiceCollection AddHandlersAndValidators(
        this IServiceCollection services, Assembly assembly, Func<Type, bool>? typeFilter = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddValidatorsFromAssembly(
            assembly, ServiceLifetime.Scoped, filter: typeFilter is null ? null : result => typeFilter(result.ValidatorType), includeInternalTypes: true);

        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && (typeFilter?.Invoke(t) ?? true)))
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
        var isCommand = handlerInterface.GetGenericTypeDefinition() == typeof(ICommandHandler<,>);
        var validationType = isCommand
            ? typeof(ValidationDecorator<,>).MakeGenericType(arguments)
            : typeof(QueryValidationDecorator<,>).MakeGenericType(arguments);
        var crossTenantType = isCommand
            ? typeof(CrossTenantDecorator<,>).MakeGenericType(arguments)
            : typeof(CrossTenantQueryDecorator<,>).MakeGenericType(arguments);
        var isCrossTenant = implementation.IsDefined(typeof(CrossTenantAttribute), inherit: false);

        // The concrete handler is registered as itself; the interface resolves to the decorator chain around it:
        // validation -> (cross-tenant check, only for [CrossTenant] handlers) -> handler (ADR-002, ADR-003 Amendment 1).
        services.AddScoped(implementation);
        services.AddScoped(
            handlerInterface,
            provider =>
            {
                var handler = provider.GetRequiredService(implementation);
                if (isCrossTenant)
                {
                    handler = ActivatorUtilities.CreateInstance(provider, crossTenantType, handler);
                }

                return ActivatorUtilities.CreateInstance(provider, validationType, handler);
            });
    }
}
