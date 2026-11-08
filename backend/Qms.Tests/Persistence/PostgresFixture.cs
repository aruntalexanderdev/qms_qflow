using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qms.Application.Common;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;
using Testcontainers.PostgreSql;

namespace Qms.Tests.Persistence;

/// <summary>An <see cref="ICurrentTenant"/> a test sets directly (the API implementation arrives with STORY-003).</summary>
public sealed class SettableCurrentTenant(Guid? companyId = null) : ICurrentTenant
{
    public Guid? CompanyId { get; set; } = companyId;
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

    /// <summary>A new, completely empty database (no migration applied).</summary>
    public async Task<string> NewBlankDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await Execute("postgres", $"CREATE DATABASE {name}");
        return ConnectionStringFor(name);
    }

    public static QmsDbContext CreateContext(string connectionString, Guid? companyId = null, TimeProvider? timeProvider = null)
    {
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connectionString).AddInterceptors(new AuditableEntityInterceptor(timeProvider ?? TimeProvider.System));
        return new QmsDbContext(builder.Options, new SettableCurrentTenant(companyId));
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
