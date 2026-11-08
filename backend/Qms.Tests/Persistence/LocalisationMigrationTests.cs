using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Infrastructure.Errors;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-051 / TASK-011 (AC-1 to AC-11): language registry, company languages, translated columns, user and dispenser language.
[Collection(PostgresCollection.Name)]
public sealed class LocalisationMigrationTests(PostgresFixture postgres)
{
    private const string PreviousMigration = "20261108072029_RowVersion";
    private const string Arabic = "البطاقات";

    private static readonly string[] TranslationColumns =
    [
        "company.name_translations", "service_type.name_translations", "priority_type.name_translations", "branch.name_translations",
        "zone.name_translations", "counter.name_translations", "ticket_template.header_translations", "ticket_template.footer_translations",
    ];

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

    private static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        var rows = await QueryAsync(connectionString, sql);
        return rows.Count == 0 ? default : (T?)rows[0][0];
    }

    private static async Task<PostgresException> RejectedAsync(string connectionString, string sql)
    {
        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connectionString, sql));
        return exception;
    }

    private static Dictionary<string, string> Json(object? jsonb) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>((string)jsonb!)!;

    private static async Task MigrateAsync(string connectionString, string? target = null)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<bool> ConstraintExistsAsync(string connectionString, string name) =>
        ScalarAsync<bool>(connectionString, $"SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{name}')");

    // ---- AC-1

    [Fact]
    public async Task Ac1_the_migration_seeds_exactly_en_and_ar_in_the_registry()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);

        var rows = await QueryAsync(connectionString, "SELECT code, native_name, direction, is_available, sort_order, created_at, updated_at FROM language ORDER BY sort_order");

        rows.Count.ShouldBe(2);
        rows[0].Take(5).ShouldBe(["en", "English", "ltr", true, 1]);
        rows[1].Take(5).ShouldBe(["ar", "العربية", "rtl", false, 2]);
        rows.ShouldAllBe(row => row[5] != null && row[6] != null);
    }

    [Fact]
    public async Task Ac1_code_is_unique_and_direction_is_ltr_or_rtl()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);

        var duplicate = await RejectedAsync(connectionString,
            "INSERT INTO language (id, code, native_name, direction, is_available, sort_order, created_at, updated_at) VALUES (gen_random_uuid(), 'en', 'English 2', 'ltr', true, 9, now(), now())");
        duplicate.SqlState.ShouldBe(SqlState.UniqueViolation);
        duplicate.ConstraintName.ShouldBe("ux_language_code");

        var direction = await RejectedAsync(connectionString,
            "INSERT INTO language (id, code, native_name, direction, is_available, sort_order, created_at, updated_at) VALUES (gen_random_uuid(), 'fr', 'Francais', 'up', false, 3, now(), now())");
        direction.SqlState.ShouldBe(SqlState.CheckViolation);
        direction.ConstraintName.ShouldBe("ck_language_direction");
    }

    [Fact]
    public async Task Ac1_the_registry_is_global_with_no_company_column_and_no_query_filter()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'language' AND column_name = 'company_id'")).ShouldBe(0);

        await using var db = PostgresFixture.CreateContext(connectionString);
        var language = db.Model.FindEntityType(typeof(Language)).ShouldNotBeNull();
        typeof(ITenantEntity).IsAssignableFrom(typeof(Language)).ShouldBeFalse();
        language.GetDeclaredQueryFilters().ShouldBeEmpty();

        // A request with no company (the login page) still reads the registry.
        await using var noTenant = PostgresFixture.CreateContext(connectionString, crossTenant: false);
        (await noTenant.Languages.CountAsync()).ShouldBe(2);
    }

    // ---- AC-2

    [Fact]
    public async Task Ac2_a_company_inserted_without_the_new_columns_gets_en_and_the_en_list()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        await ExecuteAsync(connectionString,
            "INSERT INTO company (id, name, code, currency_code, is_active, created_at, updated_at) VALUES (gen_random_uuid(), 'C', 'CX', 'AED', true, now(), now())");

        var row = (await QueryAsync(connectionString, "SELECT default_language, enabled_languages, name_translations::text FROM company"))[0];

        row[0].ShouldBe("en");
        ((string[])row[1]!).ShouldBe(["en"]);
        Json(row[2]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ac2_default_must_be_enabled_and_must_be_in_the_registry()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        const string Insert = "INSERT INTO company (id, name, code, currency_code, is_active, default_language, enabled_languages, created_at, updated_at) VALUES (gen_random_uuid(), 'C', 'CX', 'AED', true, ";

        var notEnabled = await RejectedAsync(connectionString, Insert + "'ar', '{en}', now(), now())");
        notEnabled.SqlState.ShouldBe(SqlState.CheckViolation);
        notEnabled.ConstraintName.ShouldBe("ck_company_default_language_enabled");

        var unknown = await RejectedAsync(connectionString, Insert + "'xx', '{xx}', now(), now())");
        unknown.SqlState.ShouldBe(SqlState.ForeignKeyViolation);
        unknown.ConstraintName.ShouldBe("fk_company_default_language_language_code");

        // A valid company with ar as default and enabled passes (the registry row exists; is_available is an Application rule).
        await ExecuteAsync(connectionString, Insert + "'ar', '{en,ar}', now(), now())");
    }

    [Fact]
    public async Task Ac2_a_language_referenced_by_a_company_cannot_be_deleted()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);

        var rejected = await RejectedAsync(connectionString, "DELETE FROM language WHERE code = 'en'");

        rejected.SqlState.ShouldBeOneOf(SqlState.RestrictViolation, SqlState.ForeignKeyViolation);
    }

    // ---- AC-3, AC-4, AC-6, AC-7: a database at the previous schema with data, migrated to head

    [Fact]
    public async Task Ac3_to_ac7_the_data_is_filled_correctly_when_migrating_from_the_previous_schema()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        await MigrateAsync(connectionString, PreviousMigration);
        await ExecuteAsync(connectionString, PreviousSchemaData);

        await MigrateAsync(connectionString);

        // AC-3: the five cases, in all five tables.
        foreach (var table in new[] { "company", "service_type", "priority_type", "branch", "zone" })
        {
            var rows = await QueryAsync(connectionString, $"SELECT name, name_translations::text FROM {table} ORDER BY created_at");
            rows.Count.ShouldBe(5, table);
            rows.ShouldAllBe(row => (string)row[0]! == "Cards", table);
            Json(rows[0][1]).ShouldBe(new Dictionary<string, string> { ["ar"] = Arabic }, table);
            Json(rows[1][1]).ShouldBeEmpty(table);
            Json(rows[2][1]).ShouldBeEmpty(table);
            Json(rows[3][1]).ShouldBeEmpty(table);
            Json(rows[4][1]).ShouldBe(new Dictionary<string, string> { ["ar"] = Arabic }, table);

            (await ScalarAsync<long>(connectionString,
                $"SELECT count(*) FROM information_schema.columns WHERE table_name = '{table}' AND column_name IN ('name_en', 'name_ar')")).ShouldBe(0, table);
            (await ScalarAsync<string>(connectionString,
                $"SELECT character_maximum_length::text || '/' || is_nullable FROM information_schema.columns WHERE table_name = '{table}' AND column_name = 'name'"))
                .ShouldBe("200/NO", table);
            (await ScalarAsync<string>(connectionString,
                $"SELECT column_default FROM information_schema.columns WHERE table_name = '{table}' AND column_name = 'name_translations'"))
                .ShouldBe("'{}'::jsonb", table);
        }

        // No index or constraint is named after the old columns, and the unique rules still hold.
        (await QueryAsync(connectionString,
            "SELECT n FROM (SELECT conname::text AS n FROM pg_constraint WHERE connamespace = 'public'::regnamespace UNION SELECT indexname::text FROM pg_indexes WHERE schemaname = 'public') x WHERE n LIKE '%name\\_en%' OR n LIKE '%name\\_ar%'"))
            .ShouldBeEmpty();
        (await RejectedAsync(connectionString,
            "INSERT INTO service_type (id, company_id, name, prefix, sort_order, is_active, created_at, updated_at) VALUES (gen_random_uuid(), '01930000-0000-7000-8000-000100000001', 'x', 'A', 0, true, now(), now())"))
            .SqlState.ShouldBe(SqlState.UniqueViolation);

        // AC-2 (migrated data): the existing companies are en / {en}.
        var companies = await QueryAsync(connectionString, "SELECT default_language, enabled_languages FROM company");
        companies.Count.ShouldBe(5);
        companies.ShouldAllBe(row => (string)row[0]! == "en" && ((string[])row[1]!).SequenceEqual(new[] { "en" }));

        // AC-4: ticket template (different, equal, empty Arabic) and counters.
        var templates = await QueryAsync(connectionString,
            "SELECT header, header_translations::text, footer, footer_translations::text FROM ticket_template ORDER BY created_at");
        templates.Count.ShouldBe(3);
        Json(templates[0][1]).ShouldBe(new Dictionary<string, string> { ["ar"] = "ترويسة" });
        Json(templates[0][3]).ShouldBe(new Dictionary<string, string> { ["ar"] = "تذييل" });
        Json(templates[1][1]).ShouldBeEmpty();
        Json(templates[1][3]).ShouldBeEmpty();
        Json(templates[2][1]).ShouldBeEmpty();
        Json(templates[2][3]).ShouldBeEmpty();
        templates.Select(row => (string)row[0]!).ShouldBe(["Header", "Header", "Header"]);
        templates.Select(row => (string)row[2]!).ShouldBe(["Footer", "Footer", "Footer"]);
        (await ScalarAsync<long>(connectionString,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'ticket_template' AND column_name IN ('header_en', 'header_ar', 'footer_en', 'footer_ar')")).ShouldBe(0);

        var counters = await QueryAsync(connectionString, "SELECT name, name_translations::text FROM counter ORDER BY number");
        counters.Select(row => (string)row[0]!).ShouldBe(["Counter 1", "Counter 2", "Counter 3", "Counter 4"]);
        counters.ShouldAllBe(row => Json(row[1]).Count == 0);

        // AC-6: users keep their language; the old check is gone, the FK exists with restrict, fr is refused.
        (await QueryAsync(connectionString, "SELECT language FROM asp_net_users ORDER BY user_name")).Select(row => (string)row[0]!).ShouldBe(["ar", "en"]);
        (await ConstraintExistsAsync(connectionString, "ck_asp_net_users_language")).ShouldBeFalse();
        (await ScalarAsync<string>(connectionString,
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_asp_net_users_language_language_code'")).ShouldBe("r");
        var unknownLanguage = await RejectedAsync(connectionString, UserInsert("fr-user", "fr"));
        unknownLanguage.SqlState.ShouldBe(SqlState.ForeignKeyViolation);
        unknownLanguage.ConstraintName.ShouldBe("fk_asp_net_users_language_language_code");

        // AC-7: dispenser settings.
        var settings = await QueryAsync(connectionString, "SELECT languages, default_language FROM dispenser_setting ORDER BY created_at");
        settings.Count.ShouldBe(3);
        settings.ShouldAllBe(row => ((string[])row[0]!).SequenceEqual(new[] { "en" }) && (string)row[1]! == "en");
    }

    [Fact]
    public async Task Ac7_a_dispenser_setting_created_through_the_entity_defaults_to_en()
    {
        new DispenserSetting().Languages.ShouldBe(["en"]);
        new DispenserSetting().DefaultLanguage.ShouldBe("en");

        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var deviceId = Guid.CreateVersion7();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            db.Devices.Add(new Device { Id = deviceId, CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, ZoneId = SeedIds.Zone(1), Type = Qms.Domain.Enums.DeviceType.Dispenser, Name = "Dispenser 3" });
            db.DispenserSettings.Add(new DispenserSetting { CompanyId = SeedIds.Company1, DeviceId = deviceId });
            await db.SaveChangesAsync();
        }

        await using var check = PostgresFixture.CreateContext(connectionString);
        var setting = await check.DispenserSettings.IgnoreQueryFilters().SingleAsync(s => s.DeviceId == deviceId);
        setting.Languages.ShouldBe(["en"]);
        setting.DefaultLanguage.ShouldBe("en");
    }

    // ---- AC-5

    [Fact]
    public async Task Ac5_translations_must_be_a_json_object_and_default_to_empty()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        const string Insert = "INSERT INTO service_type (id, company_id, name, prefix, sort_order, is_active, created_at, updated_at{0}) VALUES (gen_random_uuid(), '01930000-0000-7000-8000-000100000001', 'Cards', 'H', 0, true, now(), now(){1})";

        foreach (var invalid in new[] { "'[]'", "'\"x\"'", "'null'" })
        {
            var rejected = await RejectedAsync(connectionString, string.Format(Insert, ", name_translations", ", " + invalid));
            rejected.SqlState.ShouldBe(SqlState.CheckViolation, invalid);
            rejected.ConstraintName.ShouldBe("ck_service_type_name_translations_object", invalid);
        }

        await ExecuteAsync(connectionString, string.Format(Insert, string.Empty, string.Empty));
        Json(await ScalarAsync<string>(connectionString, "SELECT name_translations::text FROM service_type WHERE prefix = 'H'")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ac5_every_translations_column_is_jsonb_not_null_default_empty_with_the_object_check()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);

        foreach (var qualified in TranslationColumns)
        {
            var (table, column) = (qualified.Split('.')[0], qualified.Split('.')[1]);
            var info = (await QueryAsync(connectionString,
                $"SELECT data_type, is_nullable, column_default FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'")).Single();
            info.ShouldBe(["jsonb", "NO", "'{}'::jsonb"], qualified);

            var definition = await ScalarAsync<string>(connectionString,
                $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_{table}_{column}_object'");
            definition.ShouldNotBeNull(qualified);
            definition.ShouldContain("jsonb_typeof(" + column + ")", customMessage: qualified);
            definition.ShouldContain("'object'", customMessage: qualified);
        }
    }

    // ---- AC-8

    [Fact]
    public async Task Ac8_the_seed_is_exact_and_keeps_the_arabic_text_as_translations()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));

        var company = await db.Companies.IgnoreQueryFilters().SingleAsync();
        (company.Id, company.Name, company.Code, company.DefaultLanguage).ShouldBe((SeedIds.Company1, "Company 1", "C1", "en"));
        company.EnabledLanguages.ShouldBe(["en"]);
        company.NameTranslations.ShouldBe(new Dictionary<string, string> { ["ar"] = "الشركة 1" });

        var services = await db.ServiceTypes.IgnoreQueryFilters().OrderBy(s => s.SortOrder).ToListAsync();
        services.Select(s => (s.Id, s.Name, s.Prefix)).ShouldBe(
            Enumerable.Range(1, 6).Select(n => (SeedIds.Service(n), $"Service {n}", new[] { "A", "B", "C", "D", "F", "G" }[n - 1])));
        services.Select(s => s.NameTranslations["ar"]).ShouldBe(Enumerable.Range(1, 6).Select(n => $"الخدمة {n}"));

        var priorities = await db.PriorityTypes.IgnoreQueryFilters().OrderBy(p => p.PriorityLevel).ToListAsync();
        priorities.Select(p => (p.Id, p.Name, p.Prefix)).ShouldBe([(SeedIds.Vip, "VIP", "V"), (SeedIds.Normal, "Normal", "N")]);
        priorities.Select(p => p.NameTranslations["ar"]).ShouldBe(["كبار الشخصيات", "عادي"]);

        (await db.Branches.IgnoreQueryFilters().SingleAsync()).NameTranslations["ar"].ShouldBe("الفرع 1");
        (await db.Zones.IgnoreQueryFilters().OrderBy(z => z.SortOrder).ToListAsync()).Select(z => (z.Name, z.NameTranslations["ar"]))
            .ShouldBe([("Zone 1", "المنطقة 1"), ("Zone 2", "المنطقة 2")]);
        var counters = await db.Counters.IgnoreQueryFilters().OrderBy(c => c.Number).ToListAsync();
        counters.Select(c => c.Name).ShouldBe(["Counter 1", "Counter 2", "Counter 3", "Counter 4"]);
        counters.ShouldAllBe(c => c.NameTranslations.Count == 0);

        var template = await db.TicketTemplates.IgnoreQueryFilters().SingleAsync();
        (template.Header, template.Footer).ShouldBe(("Company 1", "Thank you for your visit"));
        (template.HeaderTranslations["ar"], template.FooterTranslations["ar"]).ShouldBe(("الشركة 1", "شكراً لزيارتكم"));

        var settings = await db.DispenserSettings.IgnoreQueryFilters().ToListAsync();
        settings.Count.ShouldBe(2);
        settings.ShouldAllBe(s => s.Languages.SequenceEqual(new[] { "en" }) && s.DefaultLanguage == "en");
    }

    // ---- AC-9

    [Fact]
    public async Task Ac9_changes_to_the_translations_object_are_tracked_and_read_back()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var id = SeedIds.Service(1);

        async Task<ServiceType> ReadAsync()
        {
            await using var reader = PostgresFixture.CreateContext(connectionString);
            return await reader.ServiceTypes.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
        }

        async Task ChangeAsync(Action<Dictionary<string, string>> change)
        {
            await using var writer = PostgresFixture.CreateContext(connectionString);
            var row = await writer.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == id);
            change(row.NameTranslations);
            (await writer.SaveChangesAsync()).ShouldBe(1);
        }

        var version = (await ReadAsync()).RowVersion;

        await ChangeAsync(t => t["fr"] = "Service un");
        var added = await ReadAsync();
        added.NameTranslations.ShouldBe(new Dictionary<string, string> { ["ar"] = "الخدمة 1", ["fr"] = "Service un" });
        added.RowVersion.ShouldNotBe(version);

        await ChangeAsync(t => t["ar"] = "خدمة 1");
        var edited = await ReadAsync();
        edited.NameTranslations["ar"].ShouldBe("خدمة 1");
        edited.RowVersion.ShouldNotBe(added.RowVersion);

        await ChangeAsync(t => t.Remove("fr"));
        var removed = await ReadAsync();
        removed.NameTranslations.ShouldBe(new Dictionary<string, string> { ["ar"] = "خدمة 1" });
        removed.RowVersion.ShouldNotBe(edited.RowVersion);

        // Assigning a new object with the same content is a change of nothing.
        await using (var same = PostgresFixture.CreateContext(connectionString))
        {
            var row = await same.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == id);
            row.NameTranslations = new Dictionary<string, string>(row.NameTranslations);
            (await same.SaveChangesAsync()).ShouldBe(0);
        }

        (await ReadAsync()).RowVersion.ShouldBe(removed.RowVersion);
    }

    [Fact]
    public async Task Ac9_an_unchanged_load_sends_no_update()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var commands = new List<string>();
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connectionString).LogTo(commands.Add, Microsoft.Extensions.Logging.LogLevel.Information);
        await using var db = new QmsDbContext(builder.Options, new SettableCurrentTenant(SeedIds.Company1));

        _ = await db.ServiceTypes.ToListAsync();
        _ = await db.TicketTemplates.ToListAsync();
        _ = await db.Counters.ToListAsync();
        (await db.SaveChangesAsync()).ShouldBe(0);

        commands.ShouldNotContain(text => text.Contains("UPDATE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ac9_no_name_en_or_name_ar_property_remains_in_the_model_or_the_snapshot()
    {
        var removed = new[] { "NameEn", "NameAr", "HeaderEn", "HeaderAr", "FooterEn", "FooterAr" };

        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: false));
        db.Model.GetEntityTypes().SelectMany(type => type.GetProperties()).Select(property => property.Name).Intersect(removed).ShouldBeEmpty();

        var snapshotType = typeof(QmsDbContext).Assembly.GetType("Qms.Infrastructure.Persistence.Migrations.QmsDbContextModelSnapshot").ShouldNotBeNull();
        var snapshot = ((ModelSnapshot)Activator.CreateInstance(snapshotType)!).Model;
        snapshot.GetEntityTypes().SelectMany(type => type.GetProperties()).Select(property => property.Name).Intersect(removed).ShouldBeEmpty();
        snapshot.FindEntityType(typeof(ServiceType))!.FindProperty(nameof(ServiceType.NameTranslations)).ShouldNotBeNull();
        snapshot.FindEntityType(typeof(Language)).ShouldNotBeNull();
    }

    // ---- AC-10

    [Fact]
    public async Task Ac10_down_restores_the_pairs_and_up_works_again()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await ExecuteAsync(connectionString,
            "INSERT INTO service_type (id, company_id, name, name_translations, prefix, sort_order, is_active, created_at, updated_at) VALUES " +
            "(gen_random_uuid(), '01930000-0000-7000-8000-000100000001', 'Cards', '{\"ar\": \"" + Arabic + "\", \"fr\": \"Cartes\"}', 'H', 7, true, now(), now()), " +
            "(gen_random_uuid(), '01930000-0000-7000-8000-000100000001', 'Loans', '{}', 'J', 8, true, now(), now())");

        await MigrateAsync(connectionString, PreviousMigration);

        var cards = (await QueryAsync(connectionString, "SELECT name_en, name_ar FROM service_type WHERE prefix = 'H'")).Single();
        cards.ShouldBe(["Cards", Arabic]);
        (await QueryAsync(connectionString, "SELECT name_en, name_ar FROM service_type WHERE prefix = 'J'")).Single().ShouldBe(["Loans", string.Empty]);
        (await QueryAsync(connectionString, "SELECT name_ar FROM service_type WHERE prefix = 'A'")).Single()[0].ShouldBe("الخدمة 1");
        (await QueryAsync(connectionString, "SELECT header_ar, footer_ar FROM ticket_template")).Single().ShouldBe(["الشركة 1", "شكراً لزيارتكم"]);
        (await ScalarAsync<string?>(connectionString,
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'service_type' AND column_name = 'name_ar'")).ShouldBeNull();

        (await ScalarAsync<string?>(connectionString, "SELECT to_regclass('language')::text")).ShouldBeNull();
        (await ScalarAsync<long>(connectionString,
            "SELECT count(*) FROM information_schema.columns WHERE (table_name = 'company' AND column_name IN ('default_language', 'enabled_languages')) OR column_name LIKE '%translations'"))
            .ShouldBe(0);
        (await ConstraintExistsAsync(connectionString, "fk_asp_net_users_language_language_code")).ShouldBeFalse();
        (await ConstraintExistsAsync(connectionString, "ck_asp_net_users_language")).ShouldBeTrue();

        await MigrateAsync(connectionString);

        (await QueryAsync(connectionString, "SELECT name_translations::text FROM service_type WHERE prefix = 'H'")).Select(row => Json(row[0])).Single()
            .ShouldBe(new Dictionary<string, string> { ["ar"] = Arabic });
        Json((await QueryAsync(connectionString, "SELECT name_translations::text FROM service_type WHERE prefix = 'J'")).Single()[0]).ShouldBeEmpty();
        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM language")).ShouldBe(2);
    }

    // ---- AC-11

    [Fact]
    public async Task Ac11_language_foreign_keys_and_the_default_check_map_to_the_catalog_answers()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);

        var user = await RejectedAsync(connectionString, UserInsert("fr-user", "fr"));
        var userError = PostgresErrorCatalog.Map(user.SqlState, user.ConstraintName).ShouldNotBeNull();
        (userError.Type, userError.Code).ShouldBe((ErrorType.Validation, "language.not_enabled"));

        const string Update = "UPDATE company SET default_language = {0}, enabled_languages = {1}";
        var check = await RejectedAsync(connectionString, string.Format(Update, "'ar'", "'{en}'"));
        check.ConstraintName.ShouldBe("ck_company_default_language_enabled");
        var checkError = PostgresErrorCatalog.Map(check.SqlState, check.ConstraintName).ShouldNotBeNull();
        (checkError.Type, checkError.Code).ShouldBe((ErrorType.Validation, ErrorCodes.RequestInvalid));

        var foreignKey = await RejectedAsync(connectionString, string.Format(Update, "'xx'", "'{xx}'"));
        foreignKey.ConstraintName.ShouldBe("fk_company_default_language_language_code");
        var foreignKeyError = PostgresErrorCatalog.Map(foreignKey.SqlState, foreignKey.ConstraintName).ShouldNotBeNull();
        (foreignKeyError.Type, foreignKeyError.Code).ShouldBe((ErrorType.Validation, ErrorCodes.RequestInvalid));

        PostgresErrorCatalog.ConstraintNames.ShouldContain("fk_asp_net_users_language_language_code");
        PostgresErrorCatalog.ConstraintNames.ShouldContain("fk_company_default_language_language_code");
        PostgresErrorCatalog.ConstraintNames.ShouldContain("ck_company_default_language_enabled");
    }

    // ---- previous-schema data (column names of the RowVersion schema)

    private static string UserInsert(string name, string language) =>
        $"INSERT INTO asp_net_users (id, display_name, language, is_active, created_at, updated_at, user_name, normalized_user_name, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) " +
        $"VALUES (gen_random_uuid(), '{name}', '{language}', true, now(), now(), '{name}', '{name.ToUpperInvariant()}', false, false, false, false, 0)";

    // Five rows per table: (a) Arabic different, (b) equal to English, (c) empty, (d) whitespace, (e) padded. created_at orders them.
    private static readonly string PreviousSchemaData = BuildPreviousSchemaData();

    private static string BuildPreviousSchemaData()
    {
        string[] arabic = [Arabic, "Cards", "", "  ", $" {Arabic} "];
        string[] ids = ["1", "2", "3", "4", "5"];
        var company = (string n) => $"01930000-0000-7000-8000-00010000000{n}";
        var branch = (string n) => $"01930000-0000-7000-8000-00020000000{n}";
        var zone = (string n) => $"01930000-0000-7000-8000-00030000000{n}";
        var sql = new System.Text.StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            var n = ids[i];
            var at = $"'2026-01-01 00:00:0{i}+00'";
            var a = arabic[i];
            sql.AppendLine($"INSERT INTO company (id, name_en, name_ar, code, currency_code, is_active, created_at, updated_at) VALUES ('{company(n)}', 'Cards', '{a}', 'C{n}', 'AED', true, {at}, {at});");
        }

        for (var i = 0; i < 5; i++)
        {
            var n = ids[i];
            var at = $"'2026-01-01 00:00:0{i}+00'";
            var a = arabic[i];
            var prefix = "ABCDE"[i];
            sql.AppendLine($"INSERT INTO service_type (id, company_id, name_en, name_ar, prefix, sort_order, is_active, created_at, updated_at) VALUES (gen_random_uuid(), '{company("1")}', 'Cards', '{a}', '{prefix}', {i}, true, {at}, {at});");
            sql.AppendLine($"INSERT INTO priority_type (id, company_id, name_en, name_ar, prefix, priority_level, is_paid, is_active, created_at, updated_at) VALUES (gen_random_uuid(), '{company("1")}', 'Cards', '{a}', '{prefix}', {i + 1}, false, true, {at}, {at});");
            sql.AppendLine($"INSERT INTO branch (id, company_id, name_en, name_ar, code, timezone, is_active, created_at, updated_at) VALUES ('{branch(n)}', '{company("1")}', 'Cards', '{a}', 'B{n}', 'Asia/Dubai', true, {at}, {at});");
        }

        for (var i = 0; i < 5; i++)
        {
            var at = $"'2026-01-01 00:00:0{i}+00'";
            sql.AppendLine($"INSERT INTO zone (id, company_id, branch_id, name_en, name_ar, sort_order, created_at, updated_at) VALUES ('{zone(ids[i])}', '{company("1")}', '{branch("1")}', 'Cards', '{arabic[i]}', {i}, {at}, {at});");
        }

        for (var number = 1; number <= 4; number++)
        {
            sql.AppendLine($"INSERT INTO counter (id, company_id, branch_id, zone_id, name, number, status, created_at, updated_at) VALUES (gen_random_uuid(), '{company("1")}', '{branch("1")}', '{zone("1")}', 'Counter {number}', {number}, 'Closed', now(), now());");
        }

        // ticket_template: Arabic different, equal to English, empty.
        (string HeaderAr, string FooterAr)[] texts = [("ترويسة", "تذييل"), ("Header", "Footer"), (string.Empty, string.Empty)];
        for (var i = 0; i < 3; i++)
        {
            var at = $"'2026-01-01 00:00:0{i}+00'";
            sql.AppendLine($"INSERT INTO ticket_template (id, company_id, header_en, header_ar, footer_en, footer_ar, show_wait, show_people_ahead, created_at, updated_at) VALUES (gen_random_uuid(), '{company("1")}', 'Header', '{texts[i].HeaderAr}', 'Footer', '{texts[i].FooterAr}', true, true, {at}, {at});");
        }

        // Dispensers: seeded form {en, ar} / en twice, and an extra setting with default ar.
        for (var i = 1; i <= 3; i++)
        {
            var device = $"01930000-0000-7000-8000-00090000000{i}";
            var at = $"'2026-01-01 00:00:0{i}+00'";
            sql.AppendLine($"INSERT INTO device (id, company_id, branch_id, zone_id, type, name, printer_status, created_at, updated_at) VALUES ('{device}', '{company("1")}', '{branch("1")}', '{zone("1")}', 'Dispenser', 'Dispenser {i}', 'Unknown', {at}, {at});");
            sql.AppendLine($"INSERT INTO dispenser_setting (id, company_id, device_id, token_mode, country_codes, languages, default_language, confirm_seconds, show_vip, created_at, updated_at) VALUES (gen_random_uuid(), '{company("1")}', '{device}', 'PrintOnly', '{{+971}}', '{{en,ar}}', '{(i == 3 ? "ar" : "en")}', 5, true, {at}, {at});");
        }

        sql.AppendLine(UserInsert("en-user", "en") + ";");
        sql.AppendLine(UserInsert("ar-user", "ar") + ";");
        return sql.ToString();
    }
}
