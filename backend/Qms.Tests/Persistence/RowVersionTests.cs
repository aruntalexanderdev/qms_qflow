using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-005 AC-36 (xmin row version, no DDL) and STORY-002 review N9 (tenant guard on every tenant entity).
[Collection(PostgresCollection.Name)]
public sealed class RowVersionTests(PostgresFixture postgres)
{
    private static IEnumerable<IEntityType> QmsEntityTypes(QmsDbContext db) =>
        db.Model.GetEntityTypes().Where(type => typeof(Entity).IsAssignableFrom(type.ClrType));

    [Fact]
    public async Task Every_entity_maps_its_row_version_to_the_xmin_system_column()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: false));

        var entityTypes = QmsEntityTypes(db).ToList();
        entityTypes.Count.ShouldBeGreaterThan(20);
        foreach (var type in entityTypes)
        {
            var property = type.FindProperty(nameof(Entity.RowVersion)).ShouldNotBeNull(type.ClrType.Name);
            property.IsConcurrencyToken.ShouldBeTrue(type.ClrType.Name);
            property.ClrType.ShouldBe(typeof(uint));
            property.GetColumnName().ShouldBe("xmin");
            property.GetColumnType().ShouldBe("xid");
            property.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
        }
    }

    [Fact]
    public async Task Every_tenant_entity_has_company_id_as_a_concurrency_token_that_cannot_change_after_save()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: false));

        var tenantTypes = db.Model.GetEntityTypes().Where(type => typeof(ITenantEntity).IsAssignableFrom(type.ClrType)).ToList();
        tenantTypes.Count.ShouldBeGreaterThan(15);
        foreach (var type in tenantTypes)
        {
            var companyId = type.FindProperty(nameof(ITenantEntity.CompanyId)).ShouldNotBeNull(type.ClrType.Name);
            companyId.IsConcurrencyToken.ShouldBeTrue(type.ClrType.Name);
            companyId.GetAfterSaveBehavior().ShouldBe(PropertySaveBehavior.Throw, type.ClrType.Name);
        }
    }

    [Fact]
    public async Task Update_commands_filter_on_both_company_id_and_xmin()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var commands = new List<string>();
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connectionString).LogTo(commands.Add, Microsoft.Extensions.Logging.LogLevel.Information).EnableSensitiveDataLogging();
        await using var db = new QmsDbContext(builder.Options, new SettableCurrentTenant(SeedIds.Company1));
        var service = await db.ServiceTypes.FirstAsync();
        service.Name = "Renamed";

        await db.SaveChangesAsync();

        var update = commands.First(text => text.Contains("UPDATE service_type", StringComparison.Ordinal));
        update.ShouldContain("company_id =");
        update.ShouldContain("xmin =");
    }

    [Fact]
    public async Task Saving_a_stale_row_version_throws_a_concurrency_exception()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var first = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        await using var second = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var a = await first.ServiceTypes.OrderBy(s => s.Prefix).FirstAsync();
        var b = await second.ServiceTypes.SingleAsync(s => s.Id == a.Id);
        var loadedVersion = a.RowVersion;
        loadedVersion.ShouldNotBe(0u);
        b.RowVersion.ShouldBe(loadedVersion);

        a.Name = "First writer";
        await first.SaveChangesAsync();
        a.RowVersion.ShouldNotBe(loadedVersion, "the new xmin is read back after the update");

        b.Name = "Second writer";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        (await check.ServiceTypes.SingleAsync(s => s.Id == a.Id)).Name.ShouldBe("First writer");
    }

    [Fact]
    public async Task A_handler_can_check_a_client_version_by_setting_the_original_row_version()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        Guid id;
        uint clientVersion;
        await using (var read = PostgresFixture.CreateContext(connectionString, SeedIds.Company1))
        {
            var service = await read.ServiceTypes.FirstAsync();
            id = service.Id;
            clientVersion = service.RowVersion;
        }

        await using (var other = PostgresFixture.CreateContext(connectionString, SeedIds.Company1))
        {
            (await other.ServiceTypes.SingleAsync(s => s.Id == id)).Name = "Changed meanwhile";
            await other.SaveChangesAsync();
        }

        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var current = await db.ServiceTypes.SingleAsync(s => s.Id == id);
        current.Name = "From a stale form";
        db.Entry(current).Property(s => s.RowVersion).OriginalValue = clientVersion;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_with_a_stale_row_version_throws()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var first = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        await using var second = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var a = await first.ServiceTypes.FirstAsync();
        var b = await second.ServiceTypes.SingleAsync(s => s.Id == a.Id);
        a.Name = "Touched";
        await first.SaveChangesAsync();

        second.ServiceTypes.Remove(b);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_migration_adds_no_columns_so_no_table_has_a_real_xmin_column()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'xmin'", connection);

        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(0);
    }

    [Fact]
    public void The_row_version_migration_contains_no_operations()
    {
        // Migrations are internal to the assembly model; ask EF for the operations of the RowVersion migration.
        var migrationType = typeof(QmsDbContext).Assembly.GetTypes().Single(type => type.Name == "RowVersion" && typeof(Microsoft.EntityFrameworkCore.Migrations.Migration).IsAssignableFrom(type));
        var migration = (Microsoft.EntityFrameworkCore.Migrations.Migration)Activator.CreateInstance(migrationType)!;

        migration.UpOperations.ShouldBeEmpty();
        migration.DownOperations.ShouldBeEmpty();
    }
}
