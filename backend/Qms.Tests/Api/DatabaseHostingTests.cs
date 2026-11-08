using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;
using Testcontainers.PostgreSql;

namespace Qms.Tests.Api;

// STORY-002 TASK-003: the API against a real PostgreSQL (AC-7 through DI, AC-8 readiness, Development-only migrate + seed).
[Collection(PostgresCollection.Name)]
public sealed class DatabaseHostingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ready_returns_200_with_the_database_up()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        using var factory = new QmsApiFactory(connectionString: connectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_returns_503_when_the_database_is_unreachable_and_live_stays_200()
    {
        // Nothing listens on port 1. Production, so the host starts without trying to migrate.
        var unreachable = QmsApiFactory.UnusedConnectionString + ";Timeout=3;Pooling=false";
        using var factory = new QmsApiFactory("Production", connectionString: unreachable);
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready", CancellationToken.None);
        var live = await client.GetAsync("/health/live", CancellationToken.None);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ready.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("Unhealthy");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_goes_from_200_to_503_when_the_database_is_stopped()
    {
        // Own container, so stopping it does not disturb the shared one.
        await using var container = new PostgreSqlBuilder("postgres:18").Build();
        await container.StartAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Pooling = false,
            Timeout = 3,
        }.ConnectionString;
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/ready", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await container.StopAsync();

        (await client.GetAsync("/health/ready", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Development_startup_migrates_an_empty_database_and_seeds_it()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using var factory = new QmsApiFactory("Development", connectionString: connectionString);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        var company = await db.Companies.IgnoreQueryFilters().SingleAsync();
        company.Id.ShouldBe(SeedIds.Company1);
        company.CurrencyCode.ShouldBe("AED");
        (await db.CounterServicePriorities.IgnoreQueryFilters().CountAsync()).ShouldBe(7);
    }

    [Fact]
    public async Task Development_startup_twice_on_the_same_database_does_not_duplicate_the_seed()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using (var first = new QmsApiFactory("Development", connectionString: connectionString))
        {
            _ = first.Services;
        }

        using var second = new QmsApiFactory("Development", connectionString: connectionString);
        using var scope = second.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        (await db.Companies.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        (await db.ServiceTypes.IgnoreQueryFilters().CountAsync()).ShouldBe(6);
    }

    [Fact]
    public async Task Production_startup_does_not_migrate_or_seed()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'", connection);
        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_connection_string_the_host_fails_to_start_with_a_clear_message()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using var factory = new QmsApiFactory("Production", connectionString: connectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Qms"] = "" })));

        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());
        exception.Message.ShouldContain("ConnectionStrings__Qms");
    }

    // AC-7 through DI: the scoped ICurrentTenant decides what the request-scoped context sees.
    [Fact]
    public async Task Scoped_current_tenant_limits_the_context_to_that_company()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        Guid company2;
        await using (var setup = PostgresFixture.CreateContext(connectionString))
        {
            var other = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
            setup.Companies.Add(other);
            setup.ServiceTypes.Add(new ServiceType { CompanyId = other.Id, NameEn = "Other", NameAr = "أخرى", Prefix = "A" });
            await setup.SaveChangesAsync();
            company2 = other.Id;
        }

        var tenant = new SettableCurrentTenant(SeedIds.Company1);
        using var factory = new QmsApiFactory("Production", connectionString: connectionString)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddScoped<ICurrentTenant>(_ => tenant)));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
            (await db.Companies.Select(c => c.Id).ToListAsync()).ShouldBe([SeedIds.Company1]);
            (await db.ServiceTypes.CountAsync()).ShouldBe(6);
        }

        tenant.CompanyId = company2;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
            (await db.ServiceTypes.Select(s => s.NameEn).ToListAsync()).ShouldBe(["Other"]);
        }
    }

    [Fact]
    public async Task Default_request_scope_has_no_company_so_tenant_queries_return_nothing()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().CompanyId.ShouldBeNull();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        (await db.ServiceTypes.CountAsync()).ShouldBe(0);
        (await db.ServiceTypes.IgnoreQueryFilters().CountAsync()).ShouldBe(6);
    }
}
