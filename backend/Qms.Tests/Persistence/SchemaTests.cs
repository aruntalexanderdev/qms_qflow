using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace Qms.Tests.Persistence;

// STORY-002 AC-1: the migration applies on an empty database and creates every table and column of spec section 9.
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres, ITestOutputHelper output)
{
    // Spec section 9 with the Phase 0 columns (VIP fee, branch_priority_fee, is_front, show_vip) and refresh_token (ADR-004).
    private static readonly Dictionary<string, string[]> SpecTables = new()
    {
        ["language"] = ["id", "created_at", "updated_at", "code", "native_name", "direction", "is_available", "sort_order"],
        ["company"] = ["id", "created_at", "updated_at", "name", "name_translations", "code", "logo_url", "currency_code", "is_active", "default_language", "enabled_languages"],
        ["service_type"] = ["id", "created_at", "updated_at", "company_id", "name", "name_translations", "prefix", "target_minutes", "sort_order", "is_active"],
        ["priority_type"] = ["id", "created_at", "updated_at", "company_id", "name", "name_translations", "prefix", "priority_level", "is_paid", "fee_amount", "is_active"],
        ["company_service_priority"] = ["id", "created_at", "updated_at", "company_id", "service_type_id", "priority_type_id"],
        ["branch"] = ["id", "created_at", "updated_at", "company_id", "name", "name_translations", "code", "timezone", "is_active"],
        ["branch_hours"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "day_of_week", "open_time", "close_time"],
        ["branch_calendar"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "date_from", "date_to", "type", "open_time", "close_time"],
        ["branch_setting"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "recall_limit", "skip_default", "block_when_no_counter", "wait_alert_minutes"],
        ["branch_priority_fee"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "priority_type_id", "fee_amount"],
        ["branch_service_priority"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "service_type_id", "priority_type_id", "max_tokens_per_day", "is_available"],
        ["zone"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "name", "name_translations", "sort_order"],
        ["counter"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "zone_id", "name", "name_translations", "number", "status", "current_user_id"],
        ["counter_service_priority"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "counter_id", "service_type_id", "priority_type_id"],
        ["service_flow"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "from_service_type_id", "to_service_type_id", "mode"],
        ["device"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "zone_id", "type", "name", "device_key_hash", "registration_code", "registration_code_expires_at", "counter_id", "last_seen_at", "printer_status"],
        ["dispenser_setting"] = ["id", "created_at", "updated_at", "company_id", "device_id", "token_mode", "country_codes", "default_country_code", "phone_min", "phone_max", "languages", "default_language", "ticket_template_id", "confirm_seconds", "show_vip"],
        ["dispenser_zone_service"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "device_id", "zone_id", "service_type_id", "sort_order"],
        ["ticket_template"] = ["id", "created_at", "updated_at", "company_id", "header", "header_translations", "footer", "footer_translations", "logo_url", "show_wait", "show_people_ahead"],
        ["token"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "business_date", "number", "priority_prefix", "service_prefix", "sequence", "priority_type_id", "current_service_type_id", "current_zone_id", "status", "customer_country_code", "customer_phone", "is_printed", "issued_device_id", "issued_by_user_id", "issued_at", "closed_at", "fee_amount", "fee_status", "fee_settled_by_user_id", "fee_settled_at", "fee_note"],
        ["token_visit"] = ["id", "created_at", "updated_at", "company_id", "token_id", "service_type_id", "priority_type_id", "zone_id", "counter_id", "user_id", "queued_at", "called_at", "recall_count", "is_front", "started_at", "ended_at", "outcome", "note"],
        ["token_sequence"] = ["id", "created_at", "updated_at", "company_id", "branch_id", "business_date", "priority_type_id", "service_type_id", "last_sequence"],
        ["audit_log"] = ["id", "created_at", "updated_at", "company_id", "user_id", "device_id", "entity", "entity_id", "action", "data", "at"],
        ["message_log"] = ["id", "created_at", "updated_at", "company_id", "token_id", "channel", "template_code", "to_phone", "status", "provider_ref", "sent_at", "error"],
        ["messaging_setting"] = ["id", "created_at", "updated_at", "company_id", "channel", "provider", "credentials", "is_enabled"],
        ["refresh_token"] = ["id", "created_at", "updated_at", "user_id", "token_hash", "family_id", "expires_at", "revoked_at", "replaced_by_id", "created_ip"],
        ["user_branch"] = ["id", "created_at", "updated_at", "company_id", "user_id", "branch_id"],
        ["user_counter"] = ["id", "created_at", "updated_at", "company_id", "user_id", "counter_id"],
        ["role"] = ["id", "created_at", "updated_at", "company_id", "name", "name_translations", "branch_scope", "is_system", "system_code", "is_active", "sort_order"],
        ["role_permission"] = ["id", "created_at", "updated_at", "company_id", "role_id", "permission"],
        ["user_role"] = ["id", "created_at", "updated_at", "company_id", "user_id", "role_id"],
    };

    private static readonly string[] IdentityTables =
    [
        "asp_net_users", "asp_net_roles", "asp_net_user_roles", "asp_net_user_claims", "asp_net_role_claims", "asp_net_user_logins", "asp_net_user_tokens",
    ];

    [Fact]
    public async Task Migration_applies_to_an_empty_database_and_leaves_nothing_pending()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);

        (await db.Database.GetPendingMigrationsAsync()).ShouldNotBeEmpty();
        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(name => name.EndsWith("_InitialSchema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_model_has_no_changes_that_are_missing_from_the_migration()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: false));

        db.Database.HasPendingModelChanges().ShouldBeFalse("add a new migration for the model change");
    }

    [Fact]
    public async Task Every_table_and_column_of_spec_section_9_exists_in_snake_case()
    {
        var columns = await QueryAsync(
            await postgres.NewDatabaseAsync(seeded: false),
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public'");

        var tables = columns.GroupBy(row => row[0]).ToDictionary(group => group.Key, group => group.Select(row => row[1]).ToHashSet());
        output.WriteLine("Tables: " + string.Join(", ", tables.Keys.Order(StringComparer.Ordinal)));

        foreach (var (table, expected) in SpecTables)
        {
            tables.ShouldContainKey(table);
            expected.Except(tables[table]).ShouldBeEmpty($"columns missing from {table}");
        }

        foreach (var table in IdentityTables)
        {
            tables.ShouldContainKey(table);
        }

        tables.Keys.Except(["__EFMigrationsHistory"]).Except(SpecTables.Keys).Except(IdentityTables).ShouldBeEmpty("unexpected tables");
        tables.Values.SelectMany(names => names).ShouldAllBe(name => name == name.ToLowerInvariant(), "all column names are snake_case");
    }

    [Fact]
    public async Task Column_types_follow_the_conventions()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        var types = (await QueryAsync(
                connectionString,
                "SELECT table_name || '.' || column_name, data_type, COALESCE(numeric_precision::text, ''), COALESCE(numeric_scale::text, '') FROM information_schema.columns WHERE table_schema = 'public'"))
            .ToDictionary(row => row[0], row => (Type: row[1], Precision: row[2], Scale: row[3]));

        // uuid keys, UTC instants, dates, money, text enums, jsonb.
        types["company.id"].Type.ShouldBe("uuid");
        types["token.created_at"].Type.ShouldBe("timestamp with time zone");
        types["token.issued_at"].Type.ShouldBe("timestamp with time zone");
        types["token.business_date"].Type.ShouldBe("date");
        types["token_sequence.business_date"].Type.ShouldBe("date");
        foreach (var money in new[] { "priority_type.fee_amount", "branch_priority_fee.fee_amount", "token.fee_amount" })
        {
            (types[money].Type, types[money].Precision, types[money].Scale).ShouldBe(("numeric", "12", "2"), money);
        }

        foreach (var text in new[] { "token.status", "token.fee_status", "counter.status", "device.type", "device.printer_status", "dispenser_setting.token_mode", "service_flow.mode", "branch_setting.skip_default", "branch_calendar.type", "token_visit.outcome", "message_log.channel", "branch_hours.day_of_week" })
        {
            types[text].Type.ShouldBe("character varying", $"{text} is stored as text");
        }

        types["audit_log.data"].Type.ShouldBe("jsonb");
        types["dispenser_setting.show_vip"].Type.ShouldBe("boolean");
    }

    [Fact]
    public async Task Show_vip_defaults_to_true_in_the_database()
    {
        var defaults = await QueryAsync(
            await postgres.NewDatabaseAsync(seeded: false),
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'dispenser_setting' AND column_name = 'show_vip'");

        defaults.Single()[0].ShouldBe("true");
    }

    [Fact]
    public async Task The_queue_index_is_partial_on_open_visits()
    {
        var definitions = await QueryAsync(
            await postgres.NewDatabaseAsync(seeded: false),
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_token_visit_queue'");

        var definition = definitions.Single()[0];
        definition.ShouldContain("(zone_id, service_type_id, priority_type_id, is_front DESC, queued_at)");
        definition.ShouldContain("WHERE (ended_at IS NULL)");
    }

    private static async Task<List<string[]>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)?.ToString() ?? string.Empty).ToArray());
        }

        return rows;
    }
}
