using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qms.Infrastructure;

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
}
