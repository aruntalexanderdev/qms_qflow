using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string in configuration (<c>ConnectionStrings:Qms</c>, env var <c>ConnectionStrings__Qms</c>).</summary>
    public const string ConnectionStringName = "Qms";

    /// <summary>
    /// Registers the clock, <see cref="QmsDbContext"/> (Npgsql, snake_case, audit-timestamp interceptor) and
    /// <see cref="DevSeeder"/>. The context needs an <c>ICurrentTenant</c> registered by the host (Qms.Api).
    /// The connection string is read when the context is first resolved, so a missing value fails with a clear
    /// message (see <see cref="GetRequiredConnectionString"/>; the API also calls it at startup).
    /// </summary>
    /// <remarks>
    /// Npgsql <c>EnableRetryOnFailure</c> is deliberately off: it forbids user-initiated transactions unless every
    /// one is wrapped in <c>CreateExecutionStrategy().ExecuteAsync</c>, and the queue engine (M3) relies on explicit
    /// transactions with <c>FOR UPDATE SKIP LOCKED</c>. Revisit with an ADR if transient faults become a problem.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Business dates use the branch timezone and storage is UTC; never call DateTime.Now (common rules section 5).
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<QmsDbContext>((serviceProvider, options) =>
        {
            // Read from the built configuration (not the captured one) so hosts and tests can override it late.
            var connectionString = GetRequiredConnectionString(serviceProvider.GetRequiredService<IConfiguration>());
            options.UseQmsPostgres(connectionString)
                .AddInterceptors(new AuditableEntityInterceptor(serviceProvider.GetRequiredService<TimeProvider>()));
        });
        services.AddScoped<DevSeeder>();
        return services;
    }

    /// <summary>Returns <c>ConnectionStrings:Qms</c> or throws with a message that names the setting.</summary>
    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings:{ConnectionStringName} " +
                $"(environment variable ConnectionStrings__{ConnectionStringName}) to a PostgreSQL connection string.")
            : value;
    }

    /// <summary>
    /// Applies pending migrations, then the idempotent development seed. For Development and test hosts only;
    /// Azure applies migrations with a bundle (ADR-006). The caller decides when this runs.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync(cancellationToken);
    }
}
