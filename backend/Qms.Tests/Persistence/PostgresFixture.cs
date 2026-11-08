using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qms.Application.Common;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Support;
using Testcontainers.PostgreSql;

namespace Qms.Tests.Persistence;

/// <summary>
/// An <see cref="ICurrentTenant"/> and <see cref="ITenantScope"/> a test sets directly. <see cref="IsCrossTenant"/> is true
/// while <see cref="ForceCrossTenant"/> is set or a section opened with <see cref="BeginCrossTenant"/> is active.
/// </summary>
public sealed class SettableCurrentTenant(Guid? companyId = null, bool crossTenant = false) : ICurrentTenant, ITenantScope
{
    private readonly CrossTenantSection _section = new();

    public Guid? CompanyId { get; set; } = companyId;

    /// <summary>Stands for a test that writes setup data with no company, like the seeder does.</summary>
    public bool ForceCrossTenant { get; set; } = crossTenant;

    public bool IsCrossTenant => ForceCrossTenant || _section.IsActive;

    public IDisposable BeginCrossTenant() => _section.Begin();
}

/// <summary>A clock tests can fix.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// One PostgreSQL 18 container per test run. The migration (and the seed) is applied once to a template database;
/// every test then gets its own copy with <c>CREATE DATABASE ... TEMPLATE</c>, so tests never see each other's rows.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string EmptyTemplate = "qms_template_empty";
    private const string SeededTemplate = "qms_template_seeded";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await Execute("postgres", $"CREATE DATABASE {EmptyTemplate}");
        await using (var db = CreateContext(ConnectionStringFor(EmptyTemplate)))
        {
            await db.Database.MigrateAsync();
        }

        await Execute("postgres", $"CREATE DATABASE {SeededTemplate} TEMPLATE {EmptyTemplate}");
        await using (var db = CreateContext(ConnectionStringFor(SeededTemplate)))
        {
            await new DevSeeder(db).SeedAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A new, isolated database: migrated and empty, or migrated and seeded. Returns its connection string.</summary>
    public async Task<string> NewDatabaseAsync(bool seeded)
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await Execute("postgres", $"CREATE DATABASE {name} TEMPLATE {(seeded ? SeededTemplate : EmptyTemplate)}");
        return ConnectionStringFor(name);
    }

    private readonly SemaphoreSlim _setupTemplateLock = new(1, 1);
    private string? _setupTemplate;

    /// <summary>
    /// A new database cloned from a template that holds the seed, the roles, the configured admin users and the
    /// <see cref="SetupTestData"/> users and rows (built once per run, so no test pays for password hashing or setup inserts).
    /// </summary>
    public async Task<string> NewSetupDatabaseAsync()
    {
        await _setupTemplateLock.WaitAsync();
        try
        {
            if (_setupTemplate is null)
            {
                var source = await NewDatabaseAsync(seeded: true);
                using (var host = AuthHost.StartOn(source))
                {
                    await SetupTestData.EnsureAsync(host);
                }

                NpgsqlConnection.ClearAllPools();
                _setupTemplate = new NpgsqlConnectionStringBuilder(source).Database!;
            }
        }
        finally
        {
            _setupTemplateLock.Release();
        }

        var name = "t_" + Guid.NewGuid().ToString("N");
        await Execute("postgres", $"CREATE DATABASE {name} TEMPLATE {_setupTemplate}");
        return ConnectionStringFor(name);
    }

    /// <summary>A new, completely empty database (no migration applied).</summary>
    public async Task<string> NewBlankDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await Execute("postgres", $"CREATE DATABASE {name}");
        return ConnectionStringFor(name);
    }

    /// <summary>
    /// A context for the tests. With no company it runs inside a cross-tenant section by default (ADR-003 Amendment 1),
    /// like the seeder, so setup code can write rows of any company; pass <c>crossTenant: false</c> to test the
    /// fail-closed behaviour of a request with no company.
    /// </summary>
    public static QmsDbContext CreateContext(
        string connectionString,
        Guid? companyId = null,
        TimeProvider? timeProvider = null,
        bool? crossTenant = null)
    {
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connectionString).AddInterceptors(new AuditableEntityInterceptor(timeProvider ?? TimeProvider.System));
        return new QmsDbContext(builder.Options, new SettableCurrentTenant(companyId, crossTenant ?? companyId is null));
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;

    private async Task Execute(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>PostgreSQL error codes used by the constraint tests.</summary>
public static class SqlState
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string CheckViolation = "23514";

    /// <summary>Raised by ON DELETE RESTRICT (NO ACTION raises 23503).</summary>
    public const string RestrictViolation = "23001";
}

public static class DbUpdateAssertions
{
    /// <summary>Saves and expects PostgreSQL to reject the change with the given SQLSTATE; returns the constraint name.</summary>
    public static async Task<string?> ShouldBeRejectedAsync(this QmsDbContext db, string sqlState)
    {
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(sqlState);
        return postgres.ConstraintName;
    }
}
