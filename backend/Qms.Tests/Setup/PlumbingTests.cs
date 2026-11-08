using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qms.Application.Auth;
using Qms.Infrastructure.Errors;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-005 AC-28 (enums), AC-29 (error catalog), AC-30 (branch scope), AC-35 (shared test data) through the real host.
[Collection(PostgresCollection.Name)]
public sealed class PlumbingTests(PostgresFixture postgres)
{
    private const string Test = "/api/v1/test/setup";

    // ---- AC-28

    [Fact]
    public async Task Ac28_enums_are_written_and_read_as_strings()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var get = await setup.GetAsync(null, Test + "/enum");
        var post = await setup.PostAsync(null, Test + "/enum", new { status = "Called" });
        var login = await setup.Host.LoginAsync(setup.Data.UserName(SetupUser.CompanyAdminC1), SetupTestData.Password);

        get.Body.ShouldContain("\"status\":\"Waiting\"");
        post.Status.ShouldBe(200);
        post.Body.ShouldContain("\"status\":\"Called\"");
        post.Json.GetProperty("isWaiting").GetBoolean().ShouldBeFalse();
        login.Status.ShouldBe(200);
        login.Json.GetProperty("user").GetProperty("roles")[0].GetProperty("name").GetString().ShouldBe("Company Admin");
    }

    // ---- AC-29

    [Theory]
    [InlineData("23505", "ix_service_type_company_id_prefix", 409, "service_type.prefix_taken")]
    [InlineData("23505", "ix_priority_type_company_id_prefix", 409, "priority_type.prefix_taken")]
    [InlineData("23505", "ix_something_unknown", 409, "resource.duplicate")]
    [InlineData("23503", "fk_company_service_priority_service_types_company_id_service_t", 409, "service_type.in_use")]
    [InlineData("23001", "fk_branch_priority_fee_priority_types_company_id_priority_type", 409, "priority_type.in_use")]
    [InlineData("23503", "fk_something_unknown", 409, "resource.in_use")]
    [InlineData("23001", "fk_something_unknown", 409, "resource.in_use")]
    [InlineData("23514", "ck_service_type_target_minutes", 400, "request.invalid")]
    [InlineData("23503", "fk_user_branch_user_company", 404, "user.not_found")]
    [InlineData("23503", "fk_user_counter_user_company", 404, "user.not_found")]
    [InlineData("23503", "fk_user_branch_branch_company_id_branch_id", 404, "resource.not_found")]
    [InlineData("23503", "fk_user_counter_counter_company_id_counter_id", 404, "resource.not_found")]
    [InlineData("concurrency", null, 409, "request.conflict")]
    public async Task Ac29_database_errors_map_by_constraint_name_and_never_leak_sql(string sqlState, string? constraint, int status, string code)
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.CallAsync(null, HttpMethod.Post, $"{Test}/db-error?sqlState={sqlState}&constraint={constraint}");

        reply.Status.ShouldBe(status, reply.Body);
        reply.Code.ShouldBe(code);
        reply.Response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        foreach (var leak in new[] { "secret_table", "INSERT", "UPDATE", "duplicate key", "violates" })
        {
            reply.Body.ShouldNotContain(leak);
        }

        if (constraint is not null)
        {
            reply.Body.ShouldNotContain(constraint);
        }
    }

    [Fact]
    public async Task Ac29_a_real_unique_violation_without_the_pre_check_is_409_prefix_taken()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Post, $"{Test}/service-types/unchecked?prefix=A");

        reply.Status.ShouldBe(409, reply.Body);
        reply.Code.ShouldBe("service_type.prefix_taken");
    }

    [Fact]
    public async Task A_bare_postgres_exception_from_raw_sql_goes_through_the_same_catalog()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Post, $"{Test}/raw-sql-duplicate");

        reply.Status.ShouldBe(409, reply.Body);
        reply.Code.ShouldBe("service_type.prefix_taken");
        reply.Body.ShouldNotContain("INSERT");
    }

    [Fact]
    public async Task Ac29_a_real_check_violation_is_400_request_invalid()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Post, $"{Test}/service-types/unchecked-invalid");

        reply.Status.ShouldBe(400, reply.Body);
        reply.Code.ShouldBe("request.invalid");
        reply.Body.ShouldNotContain("ck_service_type");
    }

    [Fact]
    public async Task Ac11_real_foreign_key_errors_without_the_pre_check_map_to_the_in_use_codes()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var service = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Delete, $"{Test}/service-types/{SeedIds.Service(1)}/unchecked");
        var priority = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Delete, $"{Test}/priority-types/{SeedIds.Vip}/unchecked");

        service.Status.ShouldBe(409, service.Body);
        service.Code.ShouldBe("service_type.in_use");
        priority.Status.ShouldBe(409, priority.Body);
        priority.Code.ShouldBe("priority_type.in_use");

        // A reference of another kind: the token FK names are in the catalog too.
        var id = SetupHost.Id(await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { name = "x", prefix = "H" }));
        await setup.AddTokenAsync(id, SeedIds.Normal);
        var token = await setup.CallAsync(SetupUser.CompanyAdminC1, HttpMethod.Delete, $"{Test}/service-types/{id}/unchecked");
        token.Code.ShouldBe("service_type.in_use");
    }

    [Fact]
    public async Task Ac29_every_name_in_the_catalog_exists_in_the_migrated_schema()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint UNION SELECT indexname FROM pg_indexes WHERE schemaname = 'public'", connection);
        var existing = new HashSet<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(0));
            }
        }

        PostgresErrorCatalog.ConstraintNames.Count.ShouldBeGreaterThan(15);
        PostgresErrorCatalog.ConstraintNames.Where(name => !existing.Contains(name)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ac29_every_foreign_key_into_service_type_and_priority_type_is_in_the_catalog()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint WHERE contype = 'f' AND confrelid IN ('service_type'::regclass, 'priority_type'::regclass)", connection);
        var foreignKeys = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                foreignKeys.Add(reader.GetString(0));
            }
        }

        // The company foreign keys are not "in use" references: nothing deletes a company here.
        var references = foreignKeys.Where(name => !name.EndsWith("_company_company_id", StringComparison.Ordinal));
        references.Where(name => !PostgresErrorCatalog.ConstraintNames.Contains(name)).ShouldBeEmpty();
    }

    // ---- AC-30

    [Fact]
    public async Task Ac30_company_admin_passes_every_branch_of_its_company()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        foreach (var branch in new[] { SeedIds.Branch1, SetupTestData.Branch2 })
        {
            var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Test}/branch-scope/{branch}");

            reply.Status.ShouldBe(200, reply.Body);
            reply.Json.GetProperty("accessible").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Ac30_branch_manager_passes_its_own_branch_only()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var own = await setup.GetAsync(SetupUser.BranchManagerB1, $"{Test}/branch-scope/{SeedIds.Branch1}");
        var other = await setup.GetAsync(SetupUser.BranchManagerB1, $"{Test}/branch-scope/{SetupTestData.Branch2}");
        var ownB2 = await setup.GetAsync(SetupUser.BranchManagerB2, $"{Test}/branch-scope/{SetupTestData.Branch2}");
        var otherB2 = await setup.GetAsync(SetupUser.BranchManagerB2, $"{Test}/branch-scope/{SeedIds.Branch1}");

        own.Status.ShouldBe(200);
        own.Json.GetProperty("accessible").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([SeedIds.Branch1]);
        other.Status.ShouldBe(403);
        other.Code.ShouldBe("auth.forbidden");
        ownB2.Json.GetProperty("accessible").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([SetupTestData.Branch2]);
        otherB2.Status.ShouldBe(403);
    }

    [Theory]
    [InlineData(SetupUser.CompanyAdminC1)]
    [InlineData(SetupUser.BranchManagerB1)]
    public async Task Ac30_a_branch_of_another_company_and_an_unknown_id_give_the_same_404(SetupUser user)
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var foreign = await setup.GetAsync(user, $"{Test}/branch-scope/{SetupTestData.Company2Branch}");
        var unknown = await setup.GetAsync(user, $"{Test}/branch-scope/{Guid.NewGuid()}");

        foreign.Status.ShouldBe(404);
        foreign.Code.ShouldBe("resource.not_found");
        unknown.Status.ShouldBe(404);
        unknown.Code.ShouldBe("resource.not_found");
        foreign.Json.GetProperty("detail").GetString().ShouldBe(unknown.Json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Ac30_the_assignments_are_read_once_per_request()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        setup.Sql.Clear();

        var reply = await setup.GetAsync(SetupUser.BranchManagerB1, $"{Test}/branch-scope/{SeedIds.Branch1}");

        reply.Status.ShouldBe(200);
        setup.Sql.Commands.Count(sql => sql.Contains("FROM user_branch", StringComparison.Ordinal)).ShouldBe(1);
    }

    // ---- AC-35

    [Fact]
    public async Task Ac35_setup_test_data_is_complete_idempotent_and_leaves_the_seed_alone()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        async Task<int[]> CountsAsync()
        {
            await using var db = setup.Host.OpenContext();
            return
            [
                await db.Companies.IgnoreQueryFilters().CountAsync(), await db.Branches.IgnoreQueryFilters().CountAsync(),
                await db.Zones.IgnoreQueryFilters().CountAsync(), await db.Counters.IgnoreQueryFilters().CountAsync(),
                await db.Devices.IgnoreQueryFilters().CountAsync(), await db.ServiceTypes.IgnoreQueryFilters().CountAsync(),
                await db.PriorityTypes.IgnoreQueryFilters().CountAsync(), await db.Users.CountAsync(),
                await db.UserBranches.IgnoreQueryFilters().CountAsync(), await db.UserCounters.IgnoreQueryFilters().CountAsync(),
                await db.CompanyServicePriorities.IgnoreQueryFilters().CountAsync(), await db.BranchServicePriorities.IgnoreQueryFilters().CountAsync(),
            ];
        }

        var first = await CountsAsync();
        var again = await SetupTestData.EnsureAsync(setup.Host);
        var second = await CountsAsync();

        second.ShouldBe(first);
        again.UserId(SetupUser.BranchManagerB1).ShouldBe(setup.Data.UserId(SetupUser.BranchManagerB1));
        // Company 1 seed: 1 + Company 2, 1 + Company 2 branch + Branch 2, services 6 + 1, priorities 2 + 1, mapping rows unchanged.
        first[0].ShouldBe(2);
        first[1].ShouldBe(3);
        first[5].ShouldBe(7);
        first[6].ShouldBe(3);
        first[10].ShouldBe(8);
        first[11].ShouldBe(7);
        await using var db = setup.Host.OpenContext();
        (await db.Counters.IgnoreQueryFilters().CountAsync(c => c.BranchId == SetupTestData.Branch2)).ShouldBe(1);
        (await db.Devices.IgnoreQueryFilters().CountAsync(d => d.BranchId == SetupTestData.Company2Branch)).ShouldBe(1);
        (await db.Devices.IgnoreQueryFilters().CountAsync(d => d.BranchId == SetupTestData.Branch2)).ShouldBe(1);
    }

    [Fact]
    public async Task Ac35_every_test_user_has_a_working_token_and_can_log_in()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var roles = new Dictionary<SetupUser, string?>
        {
            [SetupUser.CompanyAdminC1] = "Company Admin", [SetupUser.CompanyAdminC2] = "Company Admin",
            [SetupUser.BranchManagerB1] = "Branch Manager / Supervisor", [SetupUser.BranchManagerB2] = "Branch Manager / Supervisor",
            [SetupUser.CounterStaff] = "Counter Staff", [SetupUser.SuperAdmin] = null, [SetupUser.CustomRoleC1] = SetupTestData.TypeViewerName,
        };

        foreach (var (user, role) in roles)
        {
            var me = await setup.GetAsync(user, "/api/v1/auth/me");
            me.Status.ShouldBe(200, user.ToString());
            me.Json.GetProperty("isSuperAdmin").GetBoolean().ShouldBe(user == SetupUser.SuperAdmin);
            me.Json.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(role is null ? [] : [role]);
            (await setup.Host.LoginAsync(setup.Data.UserName(user), SetupTestData.Password)).Status.ShouldBe(200, user.ToString());
        }

        var manager = (await setup.Host.LoginAsync(setup.Data.UserName(SetupUser.BranchManagerB2), SetupTestData.Password)).Json.GetProperty("user");
        manager.GetProperty("branchIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([SetupTestData.Branch2]);
        var staff = (await setup.Host.LoginAsync(setup.Data.UserName(SetupUser.CounterStaff), SetupTestData.Password)).Json.GetProperty("user");
        staff.GetProperty("counterIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([SeedIds.Counter(1)]);
    }
}
