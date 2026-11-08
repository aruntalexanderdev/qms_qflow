using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-003 TASK-007: user_branch / user_counter can never cross companies or belong to a Super Admin (AC-13..AC-16, AC-23).
// The user-side constraints are raw SQL in the UserAssignmentCompany migration; these tests are what keeps them honest.
[Collection(PostgresCollection.Name)]
public sealed class UserAssignmentTests(PostgresFixture postgres)
{
    private const string InitialSchema = "20261108021220_InitialSchema";

    private sealed record World(string ConnectionString, Guid Company2, Guid Branch2, Guid Counter2, Guid User1, Guid User2, Guid SuperAdmin);

    private static AppUser NewUser(string name, Guid? companyId) =>
        new() { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.test", DisplayName = name, CompanyId = companyId };

    /// <summary>Adds Company 2 (branch, zone, counter) and three users to a database that already has the seed.</summary>
    private static async Task<World> AddWorldAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var company2 = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
        var branch2 = new Branch { CompanyId = company2.Id, NameEn = "Other branch", NameAr = "فرع آخر", Code = "X1", Timezone = "Asia/Riyadh" };
        var zone2 = new Zone { CompanyId = company2.Id, BranchId = branch2.Id, NameEn = "Main zone", NameAr = "المنطقة الرئيسية" };
        var counter2 = new Counter { CompanyId = company2.Id, BranchId = branch2.Id, ZoneId = zone2.Id, Name = "Counter X", Number = 1 };
        db.Companies.Add(company2);
        await db.SaveChangesAsync();
        db.Branches.Add(branch2);
        await db.SaveChangesAsync();
        db.Zones.Add(zone2);
        await db.SaveChangesAsync();
        db.Counters.Add(counter2);

        var user1 = NewUser("user1", SeedIds.Company1);
        var user2 = NewUser("user2", company2.Id);
        var superAdmin = NewUser("superadmin", null);
        db.Users.AddRange(user1, user2, superAdmin);
        await db.SaveChangesAsync();

        return new World(connectionString, company2.Id, branch2.Id, counter2.Id, user1.Id, user2.Id, superAdmin.Id);
    }

    private async Task<World> WorldAsync() => await AddWorldAsync(await postgres.NewDatabaseAsync(seeded: true));

    private static QmsDbContext As(World world, Guid? companyId) => PostgresFixture.CreateContext(world.ConnectionString, companyId);

    // ---- AC-13

    [Fact]
    public async Task Ac13_a_Company_2_user_cannot_be_assigned_to_Branch_1()
    {
        var world = await WorldAsync();

        // Row stamped Company 1: the branch matches, the user does not.
        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User2, BranchId = SeedIds.Branch1 });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_branch_user_company");
        }

        // Row stamped Company 2: the user matches, the branch does not.
        await using (var db = As(world, world.Company2))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User2, BranchId = SeedIds.Branch1 });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_branch_branch_company_id_branch_id");
        }

        await using var check = As(world, null);
        (await check.UserBranches.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    // ---- AC-14

    [Fact]
    public async Task Ac14_a_Company_2_user_cannot_be_assigned_to_a_Company_1_counter()
    {
        var world = await WorldAsync();

        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserCounters.Add(new UserCounter { UserId = world.User2, CounterId = SeedIds.Counter(2) });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_counter_user_company");
        }

        await using (var db = As(world, world.Company2))
        {
            db.UserCounters.Add(new UserCounter { UserId = world.User2, CounterId = SeedIds.Counter(2) });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_counter_counter_company_id_counter_id");
        }

        await using var check = As(world, null);
        (await check.UserCounters.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    // ---- AC-15

    [Fact]
    public async Task Ac15_a_Super_Admin_cannot_be_assigned_to_a_branch_or_a_counter()
    {
        var world = await WorldAsync();

        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.SuperAdmin, BranchId = SeedIds.Branch1 });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_branch_user_company");
        }

        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserCounters.Add(new UserCounter { UserId = world.SuperAdmin, CounterId = SeedIds.Counter(1) });
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_counter_user_company");
        }

        // company_id is NOT NULL, so a row without a company cannot be written either (no tenant: nothing is stamped).
        await using (var db = PostgresFixture.CreateContext(world.ConnectionString))
        {
            db.UserBranches.Add(new UserBranch { CompanyId = Guid.Empty, UserId = world.SuperAdmin, BranchId = SeedIds.Branch1 });
            await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);
        }
    }

    // ---- AC-16

    [Fact]
    public async Task Ac16_same_company_assignments_are_accepted_and_the_company_is_stamped()
    {
        var world = await WorldAsync();

        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User1, BranchId = SeedIds.Branch1 });
            db.UserCounters.Add(new UserCounter { UserId = world.User1, CounterId = SeedIds.Counter(1) });
            await db.SaveChangesAsync();
        }

        await using (var db = As(world, world.Company2))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User2, BranchId = world.Branch2 });
            db.UserCounters.Add(new UserCounter { UserId = world.User2, CounterId = world.Counter2 });
            await db.SaveChangesAsync();
        }

        await using (var db = As(world, SeedIds.Company1))
        {
            var branch = await db.UserBranches.SingleAsync();
            branch.CompanyId.ShouldBe(SeedIds.Company1);
            (await db.UserCounters.SingleAsync()).CompanyId.ShouldBe(SeedIds.Company1);
        }

        // No current company: the query filter returns nothing (stage 3 note: login lookups must use IgnoreQueryFilters).
        await using (var db = As(world, null))
        {
            (await db.UserBranches.CountAsync()).ShouldBe(0);
            (await db.UserBranches.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
        }
    }

    [Fact]
    public async Task Ac16_changing_the_company_of_an_assigned_user_is_rejected()
    {
        var world = await WorldAsync();
        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User1, BranchId = SeedIds.Branch1 });
            db.UserCounters.Add(new UserCounter { UserId = world.User1, CounterId = SeedIds.Counter(1) });
            await db.SaveChangesAsync();
        }

        await using (var db = As(world, null))
        {
            var user = await db.Users.SingleAsync(u => u.Id == world.User1);
            user.CompanyId = world.Company2;
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_branch_user_company");
        }

        await using (var db = As(world, null))
        {
            var user = await db.Users.SingleAsync(u => u.Id == world.User1);
            user.CompanyId = null; // becoming a Super Admin while assigned is rejected too
            (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_user_branch_user_company");
        }

        // Once the assignments are gone the move is allowed.
        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.RemoveRange(await db.UserBranches.ToListAsync());
            db.UserCounters.RemoveRange(await db.UserCounters.ToListAsync());
            await db.SaveChangesAsync();
        }

        await using (var db = As(world, null))
        {
            (await db.Users.SingleAsync(u => u.Id == world.User1)).CompanyId = world.Company2;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Deleting_a_user_removes_the_assignments()
    {
        var world = await WorldAsync();
        await using (var db = As(world, SeedIds.Company1))
        {
            db.UserBranches.Add(new UserBranch { UserId = world.User1, BranchId = SeedIds.Branch1 });
            await db.SaveChangesAsync();
        }

        await using (var db = As(world, null))
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == world.User1));
            await db.SaveChangesAsync();
            (await db.UserBranches.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        }
    }

    // ---- drift guard for the constraints EF does not model

    [Fact]
    public async Task The_raw_sql_constraints_exist_and_the_model_still_matches_the_snapshot()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await using var db = PostgresFixture.CreateContext(connectionString);

        // HasPendingModelChanges compares model and snapshot, and neither contains the raw constraints; this test is
        // what fails if a later migration drops or renames one of them.
        db.Database.HasPendingModelChanges().ShouldBeFalse();

        var constraints = await QueryAsync(connectionString, "SELECT conname || ' ' || contype::text FROM pg_constraint WHERE conname LIKE '%user_company%' OR conname = 'ux_asp_net_users_id_company_id'");
        constraints.ShouldBe(["fk_user_branch_user_company f", "fk_user_counter_user_company f", "ux_asp_net_users_id_company_id u"], ignoreOrder: true);

        var definitions = await QueryAsync(connectionString, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname LIKE 'fk_user_%_user_company'");
        definitions.ShouldAllBe(d => d.Contains("(user_id, company_id) REFERENCES asp_net_users(id, company_id) ON DELETE CASCADE"));
    }

    // ---- AC-23: migration

    private static async Task MigrateToAsync(string connectionString, string? target)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>A database at InitialSchema with the seed, Company 2 and users (the shape of an existing dev database).</summary>
    private async Task<World> OldWorldAsync()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        await MigrateToAsync(connectionString, InitialSchema);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await new DevSeeder(db).SeedAsync();
        }

        return await AddWorldAsync(connectionString);
    }

    private static async Task InsertOldAssignmentAsync(string connectionString, string table, string column, Guid userId, Guid targetId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"INSERT INTO {table} (id, created_at, updated_at, user_id, {column}) VALUES (@id, now(), now(), @u, @t)", connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("u", userId);
        command.Parameters.AddWithValue("t", targetId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetValue(0).ToString() ?? string.Empty);
        }

        return rows;
    }

    [Fact]
    public async Task Ac23_the_migration_backfills_company_id_of_existing_rows()
    {
        var world = await OldWorldAsync();
        await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.User1, SeedIds.Branch1);
        await InsertOldAssignmentAsync(world.ConnectionString, "user_counter", "counter_id", world.User1, SeedIds.Counter(1));
        await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.User2, world.Branch2);
        await InsertOldAssignmentAsync(world.ConnectionString, "user_counter", "counter_id", world.User2, world.Counter2);

        await MigrateToAsync(world.ConnectionString, null);

        (await QueryAsync(world.ConnectionString, "SELECT company_id FROM user_branch ORDER BY company_id")).ShouldBe(
            new[] { SeedIds.Company1, world.Company2 }.OrderBy(id => id).Select(id => id.ToString()).ToList());
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM user_counter WHERE company_id = (SELECT company_id FROM counter WHERE id = counter_id)"))
            .ShouldBe(["2"]);
        (await QueryAsync(world.ConnectionString, "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'user_branch' AND column_name = 'company_id'"))
            .ShouldBe(["NO"]);
    }

    [Theory]
    [InlineData("cross-company branch")]
    [InlineData("cross-company counter")]
    [InlineData("super admin branch")]
    public async Task Ac23_the_migration_fails_and_changes_nothing_when_an_existing_row_crosses_companies(string scenario)
    {
        var world = await OldWorldAsync();
        await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.User1, SeedIds.Branch1); // valid row
        switch (scenario)
        {
            case "cross-company branch":
                await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.User2, SeedIds.Branch1);
                break;
            case "cross-company counter":
                await InsertOldAssignmentAsync(world.ConnectionString, "user_counter", "counter_id", world.User2, SeedIds.Counter(2));
                break;
            default:
                await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.SuperAdmin, SeedIds.Branch1);
                break;
        }

        var exception = await Should.ThrowAsync<PostgresException>(() => MigrateToAsync(world.ConnectionString, null));
        exception.MessageText.ShouldContain("UserAssignmentCompany");

        // Nothing changed: no column, no constraint, migration not recorded.
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM information_schema.columns WHERE table_name IN ('user_branch', 'user_counter') AND column_name = 'company_id'")).ShouldBe(["0"]);
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM pg_constraint WHERE conname = 'ux_asp_net_users_id_company_id'")).ShouldBe(["0"]);
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%UserAssignmentCompany'")).ShouldBe(["0"]);
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM user_branch")).ShouldBe([scenario == "cross-company counter" ? "1" : "2"]);
    }

    [Fact]
    public async Task Ac23_migrating_down_removes_everything_and_up_again_works()
    {
        var world = await OldWorldAsync();
        await InsertOldAssignmentAsync(world.ConnectionString, "user_branch", "branch_id", world.User1, SeedIds.Branch1);
        await InsertOldAssignmentAsync(world.ConnectionString, "user_counter", "counter_id", world.User2, world.Counter2);

        await MigrateToAsync(world.ConnectionString, null);
        await MigrateToAsync(world.ConnectionString, InitialSchema);

        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM information_schema.columns WHERE table_name IN ('user_branch', 'user_counter') AND column_name = 'company_id'")).ShouldBe(["0"]);
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM pg_constraint WHERE conname IN ('ux_asp_net_users_id_company_id', 'fk_user_branch_user_company', 'fk_user_counter_user_company', 'ak_counter_company_id_id', 'fk_user_branch_branch_company_id_branch_id')")).ShouldBe(["0"]);
        (await QueryAsync(world.ConnectionString, "SELECT conname FROM pg_constraint WHERE conname IN ('fk_user_branch_branch_branch_id', 'fk_user_counter_counter_counter_id') ORDER BY conname"))
            .ShouldBe(["fk_user_branch_branch_branch_id", "fk_user_counter_counter_counter_id"]);
        (await QueryAsync(world.ConnectionString, "SELECT (SELECT count(*) FROM user_branch) + (SELECT count(*) FROM user_counter)")).ShouldBe(["2"]);

        await MigrateToAsync(world.ConnectionString, null);

        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM user_branch WHERE company_id IS NOT NULL")).ShouldBe(["1"]);
        (await QueryAsync(world.ConnectionString, "SELECT count(*) FROM pg_constraint WHERE conname IN ('ux_asp_net_users_id_company_id', 'fk_user_branch_user_company', 'fk_user_counter_user_company')")).ShouldBe(["3"]);
    }

    [Fact]
    public async Task The_seed_creates_no_assignments_and_is_unchanged()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        (await QueryAsync(connectionString, "SELECT (SELECT count(*) FROM user_branch) + (SELECT count(*) FROM user_counter) + (SELECT count(*) FROM asp_net_users)")).ShouldBe(["0"]);
    }
}
