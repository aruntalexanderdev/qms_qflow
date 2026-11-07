using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services. EF Core, repositories and adapters are added by later stories.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Business dates use the branch timezone and storage is UTC; never call DateTime.Now (common rules section 5).
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
