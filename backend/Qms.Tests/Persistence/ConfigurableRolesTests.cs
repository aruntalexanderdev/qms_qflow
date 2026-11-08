using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Errors;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Configurations;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Support;

namespace Qms.Tests.Persistence;

// STORY-048 / TASK-014 (AC-1 to AC-5): role, role_permission, user_role, the ConfigurableRoles data migration, its Down, and the seed.
[Collection(PostgresCollection.Name)]
public sealed class ConfigurableRolesTests(PostgresFixture postgres)
{
    private const string PreviousMigration = "20261108085816_Localisation";

    private static readonly Guid Company2 = Guid.Parse("01930000-0000-7000-8000-000100000002");
    private static readonly string[] SystemCodes = ["CompanyAdmin", "BranchManager", "CounterStaff"];

    // ---- helpers

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<object?[]>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row.Select(value => value is DBNull ? null : value).ToArray());
        }

        return rows;
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql) => (T)(await QueryAsync(connectionString, sql))[0][0]!;

    private static async Task<PostgresException> RejectedAsync(string connectionString, string sql) =>
        await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connectionString, sql));

    private static async Task MigrateAsync(string connectionString, string? target = null)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static string UserInsert(Guid id, string name, Guid? companyId) =>
        "INSERT INTO asp_net_users (id, company_id, display_name, language, is_active, created_at, updated_at, user_name, normalized_user_name, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) " +
        $"VALUES ('{id}', {(companyId is null ? "NULL" : $"'{companyId}'")}, '{name}', 'en', true, now(), now(), '{name}', '{name.ToUpperInvariant()}', false, false, false, false, 0)";

    private static string RoleInsert(Guid id, Guid companyId, string name, string scope = "Assigned", string? systemCode = null) =>
        "INSERT INTO role (id, company_id, name, branch_scope, is_system, system_code, is_active, sort_order, created_at, updated_at) " +
        $"VALUES ('{id}', '{companyId}', '{name}', '{scope}', {(systemCode is null ? "false" : "true")}, {(systemCode is null ? "NULL" : $"'{systemCode}'")}, true, 9, now(), now())";

    private static string UserRoleInsert(Guid companyId, Guid userId, Guid roleId) =>
        $"INSERT INTO user_role (id, company_id, user_id, role_id, created_at, updated_at) VALUES (gen_random_uuid(), '{companyId}', '{userId}', '{roleId}', now(), now())";

    private static string PermissionInsert(Guid companyId, Guid roleId, string permission) =>
        $"INSERT INTO role_permission (id, company_id, role_id, permission, created_at, updated_at) VALUES (gen_random_uuid(), '{companyId}', '{roleId}', '{permission}', now(), now())";

    // ---- AC-1

    [Fact]
    public async Task Ac1_the_three_tables_have_the_columns_of_the_design_and_no_name_en_or_name_ar()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);

        async Task<Dictionary<string, (string Type, string Nullable)>> ColumnsAsync(string table) =>
            (await QueryAsync(connectionString,
                $"SELECT column_name, data_type || coalesce('(' || character_maximum_length || ')', ''), is_nullable FROM information_schema.columns WHERE table_name = '{table}' AND table_schema = 'public'"))
            .ToDictionary(row => (string)row[0]!, row => ((string)row[1]!, (string)row[2]!));

        var role = await ColumnsAsync("role");
        role.Keys.ShouldBe(
            ["id", "company_id", "name", "name_translations", "branch_scope", "is_system", "system_code", "is_active", "sort_order", "created_at", "updated_at"],
            ignoreOrder: true);
        role["id"].Type.ShouldBe("uuid");
        role["name"].ShouldBe(("character varying(200)", "NO"));
        role["name_translations"].ShouldBe(("jsonb", "NO"));
        role["system_code"].Nullable.ShouldBe("YES");
        (await ScalarAsync<string>(connectionString,
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'role' AND column_name = 'name_translations'")).ShouldBe("'{}'::jsonb");

        (await ColumnsAsync("role_permission")).Keys.ShouldBe(["id", "company_id", "role_id", "permission", "created_at", "updated_at"], ignoreOrder: true);
        (await ColumnsAsync("user_role")).Keys.ShouldBe(["id", "company_id", "user_id", "role_id", "created_at", "updated_at"], ignoreOrder: true);

        // The version is the PostgreSQL system column xmin (no real column is created), mapped by the model.
        await using var db = PostgresFixture.CreateContext(connectionString);
        foreach (var type in new[] { typeof(Role), typeof(RolePermission), typeof(UserRole) })
        {
            var entity = db.Model.FindEntityType(type).ShouldNotBeNull();
            entity.FindProperty(nameof(Entity.RowVersion))!.IsConcurrencyToken.ShouldBeTrue();
            entity.FindProperty(nameof(Entity.RowVersion))!.GetColumnName().ShouldBe("xmin");
            typeof(ITenantEntity).IsAssignableFrom(type).ShouldBeTrue();
            entity.GetDeclaredQueryFilters().ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task Ac1_constraints_and_indexes_exist_under_their_fixed_names()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        var names = (await QueryAsync(connectionString, "SELECT conname FROM pg_constraint UNION SELECT indexname FROM pg_indexes WHERE schemaname = 'public'"))
            .Select(row => (string)row[0]!).ToHashSet();

        string[] expected =
        [
            "ak_role_company_id_id", "ck_role_branch_scope", "ck_role_system_code", "ck_role_is_system_matches_system_code", "ck_role_name_translations_object",
            RoleConstraints.NameUnique, RoleConstraints.SystemCodeUnique, RoleConstraints.PermissionUnique, RoleConstraints.PermissionRoleForeignKey,
            RoleConstraints.UserRoleUnique, RoleConstraints.UserRoleRoleForeignKey, RoleConstraints.UserCompanyForeignKey,
            "ix_user_role_user_id", "ix_role_permission_company_id_role_id",
        ];
        expected.Where(name => !names.Contains(name)).ShouldBeEmpty();

        // Cascade / restrict as designed: pg_constraint.confdeltype 'c' cascade, 'r' restrict.
        (await ScalarAsync<string>(connectionString, $"SELECT confdeltype::text FROM pg_constraint WHERE conname = '{RoleConstraints.PermissionRoleForeignKey}'")).ShouldBe("c");
        (await ScalarAsync<string>(connectionString, $"SELECT confdeltype::text FROM pg_constraint WHERE conname = '{RoleConstraints.UserRoleRoleForeignKey}'")).ShouldBe("r");
        (await ScalarAsync<string>(connectionString, $"SELECT confdeltype::text FROM pg_constraint WHERE conname = '{RoleConstraints.UserCompanyForeignKey}'")).ShouldBe("c");
        (await ScalarAsync<string>(connectionString,
            $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{RoleConstraints.UserCompanyForeignKey}'"))
            .ShouldBe("FOREIGN KEY (user_id, company_id) REFERENCES asp_net_users(id, company_id) ON DELETE CASCADE");
    }

    [Fact]
    public async Task Ac1_the_snapshot_has_no_pending_model_changes()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await using var db = PostgresFixture.CreateContext(connectionString);
        db.Database.HasPendingModelChanges().ShouldBeFalse("add a new migration for the model change");
    }

    [Fact]
    public async Task Ac1_role_rules_name_scope_system_code_and_translations()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var c1 = SeedIds.Company1;

        // Unique name per company ignoring case; the same name in another company is fine (the seed gave Company 1 "Counter Staff").
        var taken = await RejectedAsync(connectionString, RoleInsert(Guid.NewGuid(), c1, "counter staff"));
        (taken.SqlState, taken.ConstraintName).ShouldBe((SqlState.UniqueViolation, RoleConstraints.NameUnique));
        await ExecuteAsync(connectionString, $"INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES ('{Company2}', 'Company 2', 'C2', 'SAR', true, now(), now())");
        await ExecuteAsync(connectionString, RoleInsert(Guid.NewGuid(), Company2, "Counter Staff"));
        await ExecuteAsync(connectionString, RoleInsert(Guid.NewGuid(), c1, "Zone Supervisor"));

        // The same system_code twice in one company is refused, but any number of custom roles (system_code NULL) is fine.
        var duplicateCode = await RejectedAsync(connectionString, RoleInsert(Guid.NewGuid(), c1, "Second admin", "Company", "CompanyAdmin"));
        (duplicateCode.SqlState, duplicateCode.ConstraintName).ShouldBe((SqlState.UniqueViolation, RoleConstraints.SystemCodeUnique));
        await ExecuteAsync(connectionString, RoleInsert(Guid.NewGuid(), c1, "Another custom role"));

        var scope = await RejectedAsync(connectionString, RoleInsert(Guid.NewGuid(), c1, "Regional", "Region"));
        (scope.SqlState, scope.ConstraintName).ShouldBe((SqlState.CheckViolation, "ck_role_branch_scope"));
        var code = await RejectedAsync(connectionString, RoleInsert(Guid.NewGuid(), Company2, "Auditor", "Assigned", "Auditor"));
        (code.SqlState, code.ConstraintName).ShouldBe((SqlState.CheckViolation, "ck_role_system_code"));

        // is_system must equal (system_code is not null), both ways.
        var systemWithoutCode = await RejectedAsync(connectionString,
            "INSERT INTO role (id, company_id, name, branch_scope, is_system, system_code, is_active, sort_order, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{c1}', 'Fake system', 'Assigned', true, NULL, true, 0, now(), now())");
        systemWithoutCode.ConstraintName.ShouldBe("ck_role_is_system_matches_system_code");
        var codeWithoutSystem = await RejectedAsync(connectionString,
            "INSERT INTO role (id, company_id, name, branch_scope, is_system, system_code, is_active, sort_order, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{Company2}', 'Fake code', 'Assigned', false, 'CounterStaff', true, 0, now(), now())");
        codeWithoutSystem.ConstraintName.ShouldBe("ck_role_is_system_matches_system_code");

        var translations = await RejectedAsync(connectionString,
            "INSERT INTO role (id, company_id, name, name_translations, branch_scope, is_system, is_active, sort_order, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{c1}', 'Bad translations', '[]', 'Assigned', false, true, 0, now(), now())");
        translations.ConstraintName.ShouldBe("ck_role_name_translations_object");

        // No unique on translations: two roles may carry the same Arabic name.
        await ExecuteAsync(connectionString,
            "INSERT INTO role (id, company_id, name, name_translations, branch_scope, is_system, is_active, sort_order, created_at, updated_at) VALUES " +
            $"(gen_random_uuid(), '{c1}', 'Same ar 1', '{{\"ar\": \"x\"}}', 'Assigned', false, true, 0, now(), now()), (gen_random_uuid(), '{c1}', 'Same ar 2', '{{\"ar\": \"x\"}}', 'Assigned', false, true, 0, now(), now())");
    }

    [Fact]
    public async Task Ac1_role_permission_is_unique_per_role_and_cascades_and_a_role_held_by_a_user_cannot_be_deleted()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var c1 = SeedIds.Company1;
        var (role, user) = (Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(connectionString, RoleInsert(role, c1, "Type Viewer"));
        await ExecuteAsync(connectionString, PermissionInsert(c1, role, "service-types.view"));
        await ExecuteAsync(connectionString, PermissionInsert(c1, role, "service-types.edit"));

        var duplicate = await RejectedAsync(connectionString, PermissionInsert(c1, role, "service-types.view"));
        (duplicate.SqlState, duplicate.ConstraintName).ShouldBe((SqlState.UniqueViolation, RoleConstraints.PermissionUnique));

        // A permission of another company's role: the composite FK (company_id, role_id) refuses it.
        await ExecuteAsync(connectionString, $"INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES ('{Company2}', 'Company 2', 'C2', 'SAR', true, now(), now())");
        var crossCompany = await RejectedAsync(connectionString, PermissionInsert(Company2, role, "zones.view"));
        (crossCompany.SqlState, crossCompany.ConstraintName).ShouldBe((SqlState.ForeignKeyViolation, RoleConstraints.PermissionRoleForeignKey));

        await ExecuteAsync(connectionString, UserInsert(user, "holder", c1));
        await ExecuteAsync(connectionString, UserRoleInsert(c1, user, role));
        var held = await RejectedAsync(connectionString, $"DELETE FROM role WHERE id = '{role}'");
        (held.SqlState, held.ConstraintName).ShouldBe((SqlState.RestrictViolation, RoleConstraints.UserRoleRoleForeignKey));
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM role_permission WHERE role_id = '{role}'")).ShouldBe(2);

        await ExecuteAsync(connectionString, $"DELETE FROM user_role WHERE role_id = '{role}'");
        await ExecuteAsync(connectionString, $"DELETE FROM role WHERE id = '{role}'");
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM role_permission WHERE role_id = '{role}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Ac1_user_role_refuses_a_super_admin_and_a_cross_company_pair_and_follows_the_user_delete()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var c1 = SeedIds.Company1;
        var (superAdmin, user1, user2, role1, role2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(connectionString, $"INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES ('{Company2}', 'Company 2', 'C2', 'SAR', true, now(), now())");
        await ExecuteAsync(connectionString, RoleInsert(role1, c1, "Role 1"));
        await ExecuteAsync(connectionString, RoleInsert(role2, Company2, "Role 2"));
        await ExecuteAsync(connectionString, UserInsert(superAdmin, "sa", null));
        await ExecuteAsync(connectionString, UserInsert(user1, "user1", c1));
        await ExecuteAsync(connectionString, UserInsert(user2, "user2", Company2));

        // A Super Admin has no company, so no non-null company_id can match (id, company_id) in asp_net_users.
        var sa = await RejectedAsync(connectionString, UserRoleInsert(c1, superAdmin, role1));
        (sa.SqlState, sa.ConstraintName).ShouldBe((SqlState.ForeignKeyViolation, RoleConstraints.UserCompanyForeignKey));

        // A Company 1 user linked to a Company 2 role, whichever company_id the row carries.
        var roleSide = await RejectedAsync(connectionString, UserRoleInsert(c1, user1, role2));
        (roleSide.SqlState, roleSide.ConstraintName).ShouldBe((SqlState.ForeignKeyViolation, RoleConstraints.UserRoleRoleForeignKey));
        var userSide = await RejectedAsync(connectionString, UserRoleInsert(Company2, user1, role2));
        (userSide.SqlState, userSide.ConstraintName).ShouldBe((SqlState.ForeignKeyViolation, RoleConstraints.UserCompanyForeignKey));

        await ExecuteAsync(connectionString, UserRoleInsert(c1, user1, role1));
        await ExecuteAsync(connectionString, UserRoleInsert(Company2, user2, role2));
        var twice = await RejectedAsync(connectionString, UserRoleInsert(c1, user1, role1));
        (twice.SqlState, twice.ConstraintName).ShouldBe((SqlState.UniqueViolation, RoleConstraints.UserRoleUnique));

        await ExecuteAsync(connectionString, $"DELETE FROM asp_net_users WHERE id = '{user1}'");
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM user_role WHERE user_id = '{user1}'")).ShouldBe(0);
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM user_role WHERE user_id = '{user2}'")).ShouldBe(1);
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM role WHERE id = '{role1}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Ac1_the_catalog_maps_the_role_constraints()
    {
        PostgresErrorCatalog.Map(SqlState.UniqueViolation, RoleConstraints.NameUnique).ShouldBe(new DatabaseError(ErrorType.Conflict, "role.name_taken"));
        PostgresErrorCatalog.Map(SqlState.ForeignKeyViolation, RoleConstraints.UserCompanyForeignKey).ShouldBe(new DatabaseError(ErrorType.NotFound, "user.not_found"));
        PostgresErrorCatalog.Map(SqlState.ForeignKeyViolation, RoleConstraints.UserRoleRoleForeignKey).ShouldBe(new DatabaseError(ErrorType.NotFound, "role.not_found"));
        PostgresErrorCatalog.Map(SqlState.RestrictViolation, RoleConstraints.UserRoleRoleForeignKey).ShouldBe(new DatabaseError(ErrorType.Conflict, "role.in_use"));
        PostgresErrorCatalog.ConstraintNames.ShouldContain(RoleConstraints.NameUnique);

        // A real restrict and a real insert through the database give exactly those answers.
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var (role, user) = (Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(connectionString, RoleInsert(role, SeedIds.Company1, "Held"));
        await ExecuteAsync(connectionString, UserInsert(user, "holder", SeedIds.Company1));
        await ExecuteAsync(connectionString, UserRoleInsert(SeedIds.Company1, user, role));
        var restricted = await RejectedAsync(connectionString, $"DELETE FROM role WHERE id = '{role}'");
        PostgresErrorCatalog.Map(restricted.SqlState, restricted.ConstraintName)!.Code.ShouldBe("role.in_use");
        var missingRole = await RejectedAsync(connectionString, UserRoleInsert(SeedIds.Company1, user, Guid.NewGuid()));
        PostgresErrorCatalog.Map(missingRole.SqlState, missingRole.ConstraintName)!.Code.ShouldBe("role.not_found");
    }

    // ---- AC-2, AC-3, AC-4: migration with data

    private sealed record Previous(string ConnectionString, Guid Company2, Guid CaC1, Guid CaC2, Guid BmB1, Guid BmB2, Guid Cs, Guid Sa);

    /// <summary>
    /// A database at the Localisation migration (before the roles) with Companies 1 and 2, the Identity roles and the users
    /// CA-C1, CA-C2, BM-B1, BM-B2, CS, SA holding them, plus branch and counter assignments.
    /// </summary>
    private async Task<Previous> PreviousWorldAsync()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        await MigrateAsync(connectionString, PreviousMigration);
        var c1 = SeedIds.Company1;
        var (branch1, branch2, zone1, counter1, c2Branch) = (SeedIds.Branch1, Guid.NewGuid(), SeedIds.Zone(1), SeedIds.Counter(1), Guid.NewGuid());
        var (caC1, caC2, bmB1, bmB2, cs, sa) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var identity = SystemCodes.Append("SuperAdmin").ToDictionary(name => name, _ => Guid.NewGuid());

        List<string> statements =
        [
            $"INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES ('{c1}', 'Company 1', 'C1', 'AED', true, now(), now()), ('{Company2}', 'Company 2', 'C2', 'SAR', true, now(), now())",
            $"INSERT INTO branch (id, company_id, name, code, timezone, is_active, created_at, updated_at) VALUES ('{branch1}', '{c1}', 'Branch 1', 'B1', 'Asia/Dubai', true, now(), now()), ('{branch2}', '{c1}', 'Branch 2', 'B2', 'Asia/Dubai', true, now(), now()), ('{c2Branch}', '{Company2}', 'Branch X', 'X1', 'Asia/Riyadh', true, now(), now())",
            $"INSERT INTO zone (id, company_id, branch_id, name, sort_order, created_at, updated_at) VALUES ('{zone1}', '{c1}', '{branch1}', 'Zone 1', 1, now(), now())",
            $"INSERT INTO counter (id, company_id, branch_id, zone_id, name, number, status, created_at, updated_at) VALUES ('{counter1}', '{c1}', '{branch1}', '{zone1}', 'Counter 1', 1, 'Closed', now(), now())",
            .. identity.Select(pair => $"INSERT INTO asp_net_roles (id, name, normalized_name) VALUES ('{pair.Value}', '{pair.Key}', '{pair.Key.ToUpperInvariant()}')"),
            UserInsert(caC1, "ca.c1", c1), UserInsert(caC2, "ca.c2", Company2), UserInsert(bmB1, "bm.b1", c1), UserInsert(bmB2, "bm.b2", c1),
            UserInsert(cs, "cs", c1), UserInsert(sa, "sa", null),
            $"INSERT INTO asp_net_user_roles (user_id, role_id) VALUES ('{caC1}', '{identity["CompanyAdmin"]}'), ('{caC2}', '{identity["CompanyAdmin"]}'), ('{bmB1}', '{identity["BranchManager"]}'), ('{bmB2}', '{identity["BranchManager"]}'), ('{cs}', '{identity["CounterStaff"]}'), ('{sa}', '{identity["SuperAdmin"]}')",
            $"INSERT INTO user_branch (id, company_id, user_id, branch_id, created_at, updated_at) VALUES (gen_random_uuid(), '{c1}', '{bmB1}', '{branch1}', now(), now()), (gen_random_uuid(), '{c1}', '{bmB2}', '{branch2}', now(), now()), (gen_random_uuid(), '{c1}', '{cs}', '{branch1}', now(), now())",
            $"INSERT INTO user_counter (id, company_id, user_id, counter_id, created_at, updated_at) VALUES (gen_random_uuid(), '{c1}', '{cs}', '{counter1}', now(), now())",
        ];
        foreach (var statement in statements)
        {
            await ExecuteAsync(connectionString, statement);
        }

        return new Previous(connectionString, Company2, caC1, caC2, bmB1, bmB2, cs, sa);
    }

    private static async Task<Dictionary<string, HashSet<string>>> GrantsAsync(string connectionString, Guid companyId) =>
        (await QueryAsync(connectionString,
            $"SELECT r.system_code, p.permission FROM role r LEFT JOIN role_permission p ON p.role_id = r.id WHERE r.company_id = '{companyId}' AND r.system_code IS NOT NULL"))
        .GroupBy(row => (string)row[0]!)
        .ToDictionary(group => group.Key, group => group.Where(row => row[1] is not null).Select(row => (string)row[1]!).ToHashSet());

    [Fact]
    public async Task Ac2_the_migration_seeds_the_three_system_roles_with_the_literal_grants_for_every_company()
    {
        var world = await PreviousWorldAsync();
        await MigrateAsync(world.ConnectionString);

        foreach (var company in new[] { SeedIds.Company1, world.Company2 })
        {
            var roles = await QueryAsync(world.ConnectionString,
                $"SELECT name, system_code, branch_scope, is_system, is_active, name_translations::text, sort_order FROM role WHERE company_id = '{company}' ORDER BY sort_order");
            roles.Count.ShouldBe(3);
            roles[0].Take(5).ShouldBe(["Company Admin", "CompanyAdmin", "Company", true, true]);
            roles[1].Take(5).ShouldBe(["Branch Manager / Supervisor", "BranchManager", "Assigned", true, true]);
            roles[2].Take(5).ShouldBe(["Counter Staff", "CounterStaff", "Assigned", true, true]);
            roles.Select(row => JsonSerializer.Deserialize<Dictionary<string, string>>((string)row[5]!)!).ShouldAllBe(map => map.Count == 0);
            roles.Select(row => row[6]).ShouldBe([1, 2, 3]);

            var grants = await GrantsAsync(world.ConnectionString, company);
            grants["CompanyAdmin"].Count.ShouldBe(60);
            grants["BranchManager"].Count.ShouldBe(28);
            grants["CounterStaff"].Count.ShouldBe(0);
            grants["CompanyAdmin"].ShouldBe(Permissions.All, ignoreOrder: true);
            grants["CompanyAdmin"].ShouldContain("company-languages.view");
            grants["CompanyAdmin"].ShouldContain("company-languages.edit");
        }

        // The Branch Manager set is exactly ADR-009 section 4: view of nine modules, all actions of six.
        var manager = (await GrantsAsync(world.ConnectionString, SeedIds.Company1))["BranchManager"];
        string[] viewOnly = ["service-types", "priority-types", "service-mapping", "ticket-templates", "branches", "branch-hours", "branch-settings", "branch-fees", "service-flows"];
        string[] everything = ["branch-service-mapping", "zones", "counters", "counter-service-mapping", "devices", "dispenser-settings"];
        manager.Where(code => viewOnly.Contains(code.Split('.')[0])).ShouldBe(viewOnly.Select(module => module + ".view"), ignoreOrder: true);
        manager.Where(code => everything.Contains(code.Split('.')[0])).ShouldBe(Permissions.All.Where(code => everything.Contains(code.Split('.')[0])), ignoreOrder: true);
        manager.ShouldNotContain(code => code.StartsWith("users.") || code.StartsWith("roles.") || code.StartsWith("password-policy.") || code.StartsWith("company-languages."));

        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM role_permission")).ShouldBe(2 * (60 + 28));
    }

    [Fact]
    public async Task Ac3_existing_users_move_to_user_role_and_only_super_admin_stays_an_identity_role()
    {
        var world = await PreviousWorldAsync();
        var assignmentsBefore = await QueryAsync(world.ConnectionString, "SELECT user_id, branch_id FROM user_branch ORDER BY user_id, branch_id");
        var countersBefore = await QueryAsync(world.ConnectionString, "SELECT user_id, counter_id FROM user_counter ORDER BY user_id");
        await MigrateAsync(world.ConnectionString);

        async Task<string?> RoleOfAsync(Guid user) => (await QueryAsync(world.ConnectionString,
            $"SELECT r.company_id || '/' || r.system_code FROM user_role ur JOIN role r ON r.id = ur.role_id WHERE ur.user_id = '{user}'")).SingleOrDefault()?[0] as string;

        (await RoleOfAsync(world.CaC1)).ShouldBe($"{SeedIds.Company1}/CompanyAdmin");
        (await RoleOfAsync(world.CaC2)).ShouldBe($"{world.Company2}/CompanyAdmin");
        (await RoleOfAsync(world.BmB1)).ShouldBe($"{SeedIds.Company1}/BranchManager");
        (await RoleOfAsync(world.BmB2)).ShouldBe($"{SeedIds.Company1}/BranchManager");
        (await RoleOfAsync(world.Cs)).ShouldBe($"{SeedIds.Company1}/CounterStaff");
        (await RoleOfAsync(world.Sa)).ShouldBeNull();
        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM user_role")).ShouldBe(5);
        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM user_role ur JOIN asp_net_users u ON u.id = ur.user_id WHERE u.company_id <> ur.company_id")).ShouldBe(0);

        (await QueryAsync(world.ConnectionString, "SELECT name FROM asp_net_roles")).Select(row => row[0]).ShouldBe(["SuperAdmin"]);
        var remaining = await QueryAsync(world.ConnectionString, "SELECT user_id FROM asp_net_user_roles");
        remaining.Select(row => (Guid)row[0]!).ShouldBe([world.Sa]);

        (await QueryAsync(world.ConnectionString, "SELECT user_id, branch_id FROM user_branch ORDER BY user_id, branch_id")).Select(row => (row[0], row[1]))
            .ShouldBe(assignmentsBefore.Select(row => (row[0], row[1])));
        (await QueryAsync(world.ConnectionString, "SELECT user_id, counter_id FROM user_counter ORDER BY user_id")).Select(row => (row[0], row[1]))
            .ShouldBe(countersBefore.Select(row => (row[0], row[1])));
    }

    [Fact]
    public async Task Ac3_the_migration_refuses_and_changes_nothing_when_a_company_role_cannot_move()
    {
        var world = await PreviousWorldAsync();
        // A user without a company holding an Identity company role cannot become a user_role (no company role to join).
        var stray = Guid.NewGuid();
        await ExecuteAsync(world.ConnectionString, UserInsert(stray, "stray", null));
        await ExecuteAsync(world.ConnectionString,
            $"INSERT INTO asp_net_user_roles (user_id, role_id) SELECT '{stray}', id FROM asp_net_roles WHERE name = 'CompanyAdmin'");

        var failure = await Should.ThrowAsync<PostgresException>(() => MigrateAsync(world.ConnectionString));
        failure.MessageText.ShouldContain("cannot move to user_role");
        (await ScalarAsync<string?>(world.ConnectionString, "SELECT to_regclass('role')::text")).ShouldBeNull();
        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM asp_net_roles")).ShouldBe(4);
    }

    [Fact]
    public async Task Ac4_down_restores_the_identity_roles_and_drops_the_tables()
    {
        var world = await PreviousWorldAsync();
        await MigrateAsync(world.ConnectionString);

        // A custom role held by a user who also holds a system role, and one held by a user with no system role.
        var (custom, customOnlyUser) = (Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(world.ConnectionString, RoleInsert(custom, SeedIds.Company1, "Type Viewer"));
        await ExecuteAsync(world.ConnectionString, UserInsert(customOnlyUser, "cu.c1", SeedIds.Company1));
        await ExecuteAsync(world.ConnectionString, UserRoleInsert(SeedIds.Company1, customOnlyUser, custom));
        await ExecuteAsync(world.ConnectionString, UserRoleInsert(SeedIds.Company1, world.Cs, custom));

        await MigrateAsync(world.ConnectionString, PreviousMigration);

        (await QueryAsync(world.ConnectionString, "SELECT name FROM asp_net_roles ORDER BY name")).Select(row => (string)row[0]!)
            .ShouldBe(["BranchManager", "CompanyAdmin", "CounterStaff", "SuperAdmin"]);
        var held = (await QueryAsync(world.ConnectionString,
            "SELECT ur.user_id, ar.name FROM asp_net_user_roles ur JOIN asp_net_roles ar ON ar.id = ur.role_id"))
            .ToDictionary(row => (Guid)row[0]!, row => (string)row[1]!);
        held.OrderBy(pair => pair.Key).ToArray().ShouldBe(new Dictionary<Guid, string>
        {
            [world.CaC1] = "CompanyAdmin", [world.CaC2] = "CompanyAdmin", [world.BmB1] = "BranchManager", [world.BmB2] = "BranchManager",
            [world.Cs] = "CounterStaff", [world.Sa] = "SuperAdmin",
        }.OrderBy(pair => pair.Key).ToArray());
        held.ContainsKey(customOnlyUser).ShouldBeFalse();
        foreach (var table in new[] { "role", "role_permission", "user_role" })
        {
            (await ScalarAsync<string?>(world.ConnectionString, $"SELECT to_regclass('{table}')::text")).ShouldBeNull();
        }

        // Up again works and gives the same result.
        await MigrateAsync(world.ConnectionString);
        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM role WHERE is_system")).ShouldBe(6);
        (await ScalarAsync<long>(world.ConnectionString, "SELECT count(*) FROM user_role")).ShouldBe(5);
    }

    [Fact]
    public async Task Ac4_down_gives_a_user_with_two_system_roles_the_most_privileged_identity_role()
    {
        var world = await PreviousWorldAsync();
        await MigrateAsync(world.ConnectionString);
        var counterStaffRole = await ScalarAsync<Guid>(world.ConnectionString, $"SELECT id FROM role WHERE company_id = '{SeedIds.Company1}' AND system_code = 'CounterStaff'");
        await ExecuteAsync(world.ConnectionString, UserRoleInsert(SeedIds.Company1, world.BmB1, counterStaffRole));

        await MigrateAsync(world.ConnectionString, PreviousMigration);

        (await QueryAsync(world.ConnectionString,
            $"SELECT ar.name FROM asp_net_user_roles ur JOIN asp_net_roles ar ON ar.id = ur.role_id WHERE ur.user_id = '{world.BmB1}'")).Single()[0].ShouldBe("BranchManager");
    }

    // ---- AC-5: DefaultRoles, seed

    [Fact]
    public void Ac5_the_catalog_has_60_distinct_codes_and_the_default_counts_are_60_28_0()
    {
        Permissions.All.Count.ShouldBe(60);
        Permissions.All.Distinct().Count().ShouldBe(60);
        Permissions.All.ShouldAllBe(code => code.Contains('.') && code == code.ToLowerInvariant());
        Permissions.All.Select(code => code.Split('.')[0]).Distinct().Count().ShouldBe(19);
        // Every action needs the module's view (ADR-009 section 3), also in the defaults.
        foreach (var definition in DefaultRoles.Definitions)
        {
            definition.Permissions.Where(code => !code.EndsWith(".view", StringComparison.Ordinal))
                .Where(code => !definition.Permissions.Contains(code.Split('.')[0] + ".view")).ShouldBeEmpty();
        }

        DefaultRoles.Definitions.Select(definition => definition.Permissions.Count).ShouldBe([60, 28, 0]);
        DefaultRoles.Definitions.Select(definition => definition.Name).ShouldBe(["Company Admin", "Branch Manager / Supervisor", "Counter Staff"]);
        DefaultRoles.Definitions.Select(definition => definition.BranchScope).ShouldBe([RoleBranchScope.Company, RoleBranchScope.Assigned, RoleBranchScope.Assigned]);
        DefaultRoles.Definitions.Select(definition => definition.SortOrder).ShouldBe([1, 2, 3]);
        DefaultRoles.Definitions.ShouldAllBe(definition => definition.Permissions.Distinct().Count() == definition.Permissions.Count);
    }

    [Fact]
    public async Task Ac5_default_roles_equal_what_the_migration_wrote()
    {
        var world = await PreviousWorldAsync();
        await MigrateAsync(world.ConnectionString);
        var migrated = await GrantsAsync(world.ConnectionString, SeedIds.Company1);
        var migratedRoles = (await QueryAsync(world.ConnectionString,
            $"SELECT system_code, name, branch_scope, sort_order, is_system, is_active FROM role WHERE company_id = '{SeedIds.Company1}'"))
            .ToDictionary(row => (string)row[0]!);

        // Roles that DefaultRoles creates for a brand-new company, written to a database and read back.
        var newCompany = Guid.NewGuid();
        await ExecuteAsync(world.ConnectionString, $"INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES ('{newCompany}', 'New', 'NW', 'AED', true, now(), now())");
        await using (var db = PostgresFixture.CreateContext(world.ConnectionString))
        {
            db.CompanyRoles.AddRange(DefaultRoles.CreateFor(newCompany));
            await db.SaveChangesAsync();
        }

        var created = await GrantsAsync(world.ConnectionString, newCompany);
        created.Keys.ShouldBe(SystemCodes, ignoreOrder: true);
        foreach (var code in SystemCodes)
        {
            created[code].ShouldBe(migrated[code], ignoreOrder: true, customMessage: code);
        }

        foreach (var definition in DefaultRoles.Definitions)
        {
            var row = migratedRoles[definition.Code.ToString()];
            row.Skip(1).ShouldBe([definition.Name, definition.BranchScope.ToString(), definition.SortOrder, true, true]);
            definition.Permissions.ShouldBe(migrated[definition.Code.ToString()], ignoreOrder: true);
        }
    }

    [Fact]
    public async Task Ac5_the_dev_seed_gives_company_1_the_system_roles_and_the_company_admin_holds_one_through_user_role()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var admin = await host.SeededUserIdAsync(TestAccounts.CompanyAdminUserName);
        var superAdmin = await host.SeededUserIdAsync(TestAccounts.SuperAdminUserName);
        var connectionString = host.ConnectionString;

        var grants = await GrantsAsync(connectionString, SeedIds.Company1);
        grants.Select(pair => (pair.Key, pair.Value.Count)).ShouldBe([("CompanyAdmin", 60), ("BranchManager", 28), ("CounterStaff", 0)], ignoreOrder: true);
        foreach (var definition in DefaultRoles.Definitions)
        {
            grants[definition.Code.ToString()].ShouldBe(definition.Permissions, ignoreOrder: true);
        }

        (await QueryAsync(connectionString, $"SELECT r.system_code FROM user_role ur JOIN role r ON r.id = ur.role_id WHERE ur.user_id = '{admin}'"))
            .Select(row => row[0]).ShouldBe(["CompanyAdmin"]);
        (await ScalarAsync<long>(connectionString, $"SELECT count(*) FROM user_role WHERE user_id = '{superAdmin}'")).ShouldBe(0);
        (await QueryAsync(connectionString, "SELECT name FROM asp_net_roles")).Select(row => row[0]).ShouldBe(["SuperAdmin"]);
        (await QueryAsync(connectionString, $"SELECT user_id FROM asp_net_user_roles")).Select(row => (Guid)row[0]!).ShouldBe([superAdmin]);

        // Running the seeders again changes nothing.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await new DevSeeder(db).SeedAsync();
        }

        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM role")).ShouldBe(3);
        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM role_permission")).ShouldBe(88);
    }

    [Fact]
    public async Task Ac5_the_seeded_template_database_has_the_roles_of_company_1_and_no_tenant_row_leaks_to_other_companies()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var company1 = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        (await company1.CompanyRoles.CountAsync()).ShouldBe(3);
        (await company1.RolePermissions.CountAsync()).ShouldBe(88);

        await using var other = PostgresFixture.CreateContext(connectionString, Company2);
        (await other.CompanyRoles.CountAsync()).ShouldBe(0);
        (await other.RolePermissions.CountAsync()).ShouldBe(0);
        (await other.CompanyUserRoles.CountAsync()).ShouldBe(0);
    }
}
