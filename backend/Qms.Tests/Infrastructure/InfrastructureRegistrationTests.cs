using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Common;
using InfrastructureRegistration = Qms.Infrastructure.DependencyInjection;
using Qms.Infrastructure;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;

namespace Qms.Tests.Infrastructure;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registers_the_system_time_provider()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddInfrastructure_registers_the_context_and_the_seeder_for_a_configured_connection_string()
    {
        using var provider = BuildProvider("Host=localhost;Database=x;Username=x;Password=x");

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<QmsDbContext>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<DevSeeder>().ShouldNotBeNull();
    }

    [Fact]
    public void Resolving_the_context_without_a_connection_string_fails_with_a_clear_message()
    {
        using var provider = BuildProvider(null);

        using var scope = provider.CreateScope();
        var exception = Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<QmsDbContext>());
        exception.Message.ShouldContain("ConnectionStrings__Qms");
    }

    [Fact]
    public void GetRequiredConnectionString_fails_fast_when_blank()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Qms"] = " " })
            .Build();

        Should.Throw<InvalidOperationException>(() => InfrastructureRegistration.GetRequiredConnectionString(configuration))
            .Message.ShouldContain("ConnectionStrings:Qms");
    }

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Qms"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ICurrentTenant>(_ => new SettableCurrentTenant());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
