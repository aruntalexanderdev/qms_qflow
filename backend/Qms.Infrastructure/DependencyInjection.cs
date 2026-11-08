using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Infrastructure.Authorization;
using Qms.Application.Localisation;
using Qms.Infrastructure.Identity;
using Qms.Infrastructure.Localisation;
using Qms.Infrastructure.Notifications;
using Qms.Infrastructure.Scope;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string in configuration (<c>ConnectionStrings:Qms</c>, env var <c>ConnectionStrings__Qms</c>).</summary>
    public const string ConnectionStringName = "Qms";

    /// <summary>
    /// Registers the clock, <see cref="QmsDbContext"/> (Npgsql, snake_case, audit-timestamp interceptor) and
    /// <see cref="DevSeeder"/>, Identity (users, roles, lockout), the JWT and refresh token services and
    /// <see cref="IdentitySeeder"/>. The host (Qms.Api) must register <c>ICurrentTenant</c> and <c>ITenantScope</c>.
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
        // The port the use cases see (ADR-008 section 1): the same scoped context instance.
        services.AddScoped<IQmsDbContext>(provider => provider.GetRequiredService<QmsDbContext>());
        services.AddScoped<IBranchScope, BranchScope>();
        // One query per request for the registry and the company languages (ADR-010, AC-26).
        services.AddScoped<ICompanyLanguages, CompanyLanguages>();
        // Permissions and branch scope come from the user's active roles, read once per request (STORY-048, ADR-009 section 6).
        services.AddScoped<EffectiveAccessReader>();
        services.AddScoped<IEffectiveAccessReader>(provider => provider.GetRequiredService<EffectiveAccessReader>());
        services.AddScoped<IPermissionResolver, DatabaseRolePermissionResolver>();
        services.AddScoped<IAdminContinuityStore, AdminContinuityStore>();
        // No-op until STORY-021 (SignalR DeviceConfigChanged); a host or test may register its own before or after.
        services.TryAddSingleton<IDeviceConfigNotifier, NoOpDeviceConfigNotifier>();
        services.AddScoped<DevSeeder>();
        services.AddQmsIdentity();
        return services;
    }

    private static void AddQmsIdentity(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        // Core only (no cookies, no SignInManager): the API signs in through JWT. Lockout: 5 failures, 15 minutes (ADR-004).
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<QmsDbContext>();

        services.AddScoped<UserAccounts>();
        services.AddScoped<IUserAccounts>(provider => provider.GetRequiredService<UserAccounts>());
        services.AddScoped<IUserDirectory>(provider => provider.GetRequiredService<UserAccounts>());
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IdentitySeeder>();
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
    /// Applies pending migrations, then the idempotent development seed (example data, roles and the configured
    /// admin users) inside a cross-tenant section (ADR-003 Amendment 1). For Development and test hosts only; Azure
    /// applies migrations with a bundle (ADR-006). The caller decides when this runs.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        // One of the three places allowed to open a cross-tenant section: the seeder writes every company's rows.
        using (scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginCrossTenant())
        {
            await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(cancellationToken);
        }
    }
}
