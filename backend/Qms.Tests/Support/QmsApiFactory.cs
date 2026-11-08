using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qms.Api.Common;
using Qms.Application.Common;
using Serilog.Core;

namespace Qms.Tests.Support;

/// <summary>Starts the real API host with the test-only controllers, handlers and an in-memory log sink.</summary>
public sealed class QmsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Connection string used by hosts that do not touch the database. Nothing listens on port 1.</summary>
    public const string UnusedConnectionString = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused";

    /// <param name="environment">Hosting environment.</param>
    /// <param name="failingReadyCheck">Adds a ready check that always fails.</param>
    /// <param name="connectionString">
    /// A real database (Testcontainers) for tests that need one. When null the host gets an unused connection
    /// string, skips the Development migrate-and-seed and drops the database readiness check, so tests that do
    /// not need a database also do not need Docker.
    /// </param>
    public QmsApiFactory(string environment = "Development", bool failingReadyCheck = false, string? connectionString = null)
    {
        Environment = environment;
        FailingReadyCheck = failingReadyCheck;
        ConnectionString = connectionString;
    }

    public string? ConnectionString { get; }

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
                ["ConnectionStrings:Qms"] = ConnectionString ?? UnusedConnectionString,
                ["Database:MigrateOnStartup"] = ConnectionString is null ? "false" : "true",
            }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            services.AddControllers().AddApplicationPart(typeof(QmsApiFactory).Assembly);
            services.AddHandlersAndValidators(typeof(QmsApiFactory).Assembly);
            if (ConnectionString is null)
            {
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    var database = options.Registrations.Single(registration => registration.Name == DatabaseHealthCheck.Name);
                    options.Registrations.Remove(database);
                });
            }

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
