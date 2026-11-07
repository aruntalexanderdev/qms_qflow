using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qms.Application.Common;
using Serilog.Core;

namespace Qms.Tests.Support;

/// <summary>Starts the real API host with the test-only controllers, handlers and an in-memory log sink.</summary>
public sealed class QmsApiFactory : WebApplicationFactory<Program>
{
    public QmsApiFactory(string environment = "Development", bool failingReadyCheck = false)
    {
        Environment = environment;
        FailingReadyCheck = failingReadyCheck;
    }

    public string Environment { get; }

    public bool FailingReadyCheck { get; }

    public InMemorySink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        // No file sink in tests: hosts would share one logs/ file. Turn sink 1 (File in appsettings.json) into a
        // console sink that only emits Fatal events; the in-memory sink is added below.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:1:Name"] = "Console",
                ["Serilog:WriteTo:1:Args:path"] = null,
                ["Serilog:WriteTo:1:Args:rollingInterval"] = null,
                ["Serilog:WriteTo:1:Args:retainedFileCountLimit"] = null,
                ["Serilog:WriteTo:1:Args:restrictedToMinimumLevel"] = "Fatal",
            }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            services.AddControllers().AddApplicationPart(typeof(QmsApiFactory).Assembly);
            services.AddHandlersAndValidators(typeof(QmsApiFactory).Assembly);
            if (FailingReadyCheck)
            {
                services.AddHealthChecks().AddCheck(
                    "broken-dependency",
                    () => HealthCheckResult.Unhealthy("down"),
                    tags: ["ready"]);
            }
        });
    }
}
