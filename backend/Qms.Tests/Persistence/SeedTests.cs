using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-002 AC-2: the seed matches spec section 4 exactly and is idempotent.
[Collection(PostgresCollection.Name)]
public sealed class SeedTests(PostgresFixture postgres)
{
    private async Task<SeedHandle> SeededAsync(int runs = 1)
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: false);
        for (var run = 0; run < runs; run++)
        {
            await using var db = PostgresFixture.CreateContext(connectionString);
            await new DevSeeder(db).SeedAsync();
        }

        return new SeedHandle(PostgresFixture.CreateContext(connectionString));
    }

    [Fact]
    public async Task Company_mapping_has_the_8_rows_of_spec_4_1()
    {
        await using var handle = await SeededAsync();

        var rows = (await handle.Db.CompanyServicePriorities.IgnoreQueryFilters().ToListAsync())
            .Select(m => ServiceName(m.ServiceTypeId) + " " + PriorityName(m.PriorityTypeId));

        rows.ShouldBe(
            ["Service 1 N", "Service 2 N", "Service 2 V", "Service 3 N", "Service 3 V", "Service 4 N", "Service 5 N", "Service 6 N"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Services_and_priority_types_match_the_spec_tables()
    {
        await using var handle = await SeededAsync();

        var services = await handle.Db.ServiceTypes.IgnoreQueryFilters().OrderBy(s => s.SortOrder).Select(s => new { s.NameEn, s.Prefix }).ToListAsync();
        services.Select(s => (s.NameEn, s.Prefix)).ShouldBe(
            [("Service 1", "A"), ("Service 2", "B"), ("Service 3", "C"), ("Service 4", "D"), ("Service 5", "F"), ("Service 6", "G")]);

        var priorities = await handle.Db.PriorityTypes.IgnoreQueryFilters().OrderBy(p => p.PriorityLevel).ToListAsync();
        priorities.Count.ShouldBe(2);
        var vip = priorities[0];
        (vip.NameEn, vip.Prefix, vip.PriorityLevel, vip.IsPaid, vip.FeeAmount).ShouldBe(("VIP", "V", 1, true, 50.00m));
        var normal = priorities[1];
        (normal.NameEn, normal.Prefix, normal.PriorityLevel, normal.IsPaid, normal.FeeAmount).ShouldBe(("Normal", "N", 2, false, null));

        (await handle.Db.Companies.IgnoreQueryFilters().SingleAsync()).CurrencyCode.ShouldBe("AED");
        services.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Branch_mapping_has_the_7_rows_of_spec_4_2_all_available()
    {
        await using var handle = await SeededAsync();

        var rows = await handle.Db.BranchServicePriorities.IgnoreQueryFilters().ToListAsync();

        rows.Count.ShouldBe(7);
        rows.ShouldAllBe(row => row.IsAvailable && row.MaxTokensPerDay == 100 && row.BranchId == SeedIds.Branch1);
        rows.Select(m => ServiceName(m.ServiceTypeId) + " " + PriorityName(m.PriorityTypeId)).ShouldBe(
            ["Service 1 N", "Service 2 N", "Service 2 V", "Service 3 N", "Service 3 V", "Service 4 N", "Service 5 N"], ignoreOrder: true);
    }

    [Fact]
    public async Task Counter_mapping_has_the_7_rows_of_spec_4_3_including_counter_2_service_3_vip()
    {
        await using var handle = await SeededAsync();

        var rows = await handle.Db.CounterServicePriorities.IgnoreQueryFilters().ToListAsync();

        rows.Select(m => $"Counter {CounterNumber(m.CounterId)} {ServiceName(m.ServiceTypeId)} {PriorityName(m.PriorityTypeId)}").ShouldBe(
            [
                "Counter 1 Service 1 N", "Counter 1 Service 2 N", "Counter 1 Service 3 N",
                "Counter 2 Service 2 V", "Counter 2 Service 3 V",
                "Counter 3 Service 4 N", "Counter 4 Service 5 N",
            ],
            ignoreOrder: true);

        var counters = await handle.Db.Counters.IgnoreQueryFilters().OrderBy(c => c.Number).ToListAsync();
        counters.Select(c => (c.Name, c.ZoneId)).ShouldBe(
            [("Counter 1", SeedIds.Zone(1)), ("Counter 2", SeedIds.Zone(1)), ("Counter 3", SeedIds.Zone(2)), ("Counter 4", SeedIds.Zone(2))]);
        (await handle.Db.Zones.IgnoreQueryFilters().Select(z => z.NameEn).ToListAsync()).ShouldBe(["Zone 1", "Zone 2"], ignoreOrder: true);
    }

    [Fact]
    public async Task Dispenser_mapping_has_the_5_rows_of_spec_4_4_and_dispenser_settings()
    {
        await using var handle = await SeededAsync();

        var rows = await handle.Db.DispenserZoneServices.IgnoreQueryFilters().ToListAsync();
        rows.Select(m => (DispenserNumber(m.DeviceId), ZoneNumber(m.ZoneId), ServiceName(m.ServiceTypeId))).ShouldBe(
            [(1, 1, "Service 1"), (1, 1, "Service 2"), (2, 1, "Service 3"), (2, 2, "Service 4"), (2, 2, "Service 5")], ignoreOrder: true);

        var devices = await handle.Db.Devices.IgnoreQueryFilters().OrderBy(d => d.Name).ToListAsync();
        devices.Select(d => (d.Name, d.Type)).ShouldBe([("Token Dispenser 1", DeviceType.Dispenser), ("Token Dispenser 2", DeviceType.Dispenser)]);

        var settings = await handle.Db.DispenserSettings.IgnoreQueryFilters().ToListAsync();
        settings.Count.ShouldBe(2);
        settings.ShouldAllBe(s => s.TokenMode == TokenMode.PrintOnly && s.ShowVip && s.DefaultLanguage == "en");
        settings.ShouldAllBe(s => s.Languages.SequenceEqual(new[] { "en", "ar" }));
    }

    [Fact]
    public async Task Branch_1_has_timezone_sample_hours_settings_and_one_bilingual_ticket_template()
    {
        await using var handle = await SeededAsync();

        var branch = await handle.Db.Branches.IgnoreQueryFilters().SingleAsync();
        (branch.NameEn, branch.NameAr, branch.Timezone).ShouldBe(("Branch 1", "الفرع 1", "Asia/Dubai"));

        var hours = await handle.Db.BranchHours.IgnoreQueryFilters().ToListAsync();
        hours.Select(h => h.DayOfWeek).ShouldBe(
            [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday], ignoreOrder: true);
        hours.ShouldAllBe(h => h.OpenTime == new TimeOnly(8, 0) && h.CloseTime == new TimeOnly(15, 0));

        var setting = await handle.Db.BranchSettings.IgnoreQueryFilters().SingleAsync();
        (setting.RecallLimit, setting.SkipDefault).ShouldBe((3, SkipDefault.NoShow));

        var template = await handle.Db.TicketTemplates.IgnoreQueryFilters().SingleAsync();
        new[] { template.HeaderEn, template.HeaderAr, template.FooterEn, template.FooterAr }.ShouldAllBe(text => !string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task Names_exist_in_english_and_arabic()
    {
        await using var handle = await SeededAsync();

        (await handle.Db.ServiceTypes.IgnoreQueryFilters().AllAsync(s => s.NameAr != "" && s.NameEn != "")).ShouldBeTrue();
        (await handle.Db.PriorityTypes.IgnoreQueryFilters().AllAsync(s => s.NameAr != "" && s.NameEn != "")).ShouldBeTrue();
        (await handle.Db.Zones.IgnoreQueryFilters().AllAsync(s => s.NameAr != "" && s.NameEn != "")).ShouldBeTrue();
    }

    [Fact]
    public async Task Running_the_seed_a_second_time_creates_no_duplicates()
    {
        await using var once = await SeededAsync(runs: 1);
        await using var twice = await SeededAsync(runs: 2);

        (await CountsAsync(twice.Db)).ShouldBe(await CountsAsync(once.Db));
        (await CountsAsync(twice.Db))["company_service_priority"].ShouldBe(8);
        (await CountsAsync(twice.Db))["branch_service_priority"].ShouldBe(7);
        (await CountsAsync(twice.Db))["counter_service_priority"].ShouldBe(7);
        (await CountsAsync(twice.Db))["dispenser_zone_service"].ShouldBe(5);
    }

    [Fact]
    public async Task A_second_run_does_not_overwrite_changes_made_after_the_first()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var vip = await db.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Prefix == "V");
            vip.FeeAmount = 75m;
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await new DevSeeder(db).SeedAsync();
        }

        await using var check = PostgresFixture.CreateContext(connectionString);
        (await check.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Prefix == "V")).FeeAmount.ShouldBe(75m);
    }

    [Fact]
    public async Task The_seed_does_not_create_users_or_roles()
    {
        await using var handle = await SeededAsync();

        (await handle.Db.Users.CountAsync()).ShouldBe(0);
        (await handle.Db.Roles.CountAsync()).ShouldBe(0);
    }

    private static async Task<Dictionary<string, int>> CountsAsync(QmsDbContext db) => new()
    {
        ["company"] = await db.Companies.IgnoreQueryFilters().CountAsync(),
        ["service_type"] = await db.ServiceTypes.IgnoreQueryFilters().CountAsync(),
        ["priority_type"] = await db.PriorityTypes.IgnoreQueryFilters().CountAsync(),
        ["company_service_priority"] = await db.CompanyServicePriorities.IgnoreQueryFilters().CountAsync(),
        ["branch"] = await db.Branches.IgnoreQueryFilters().CountAsync(),
        ["branch_hours"] = await db.BranchHours.IgnoreQueryFilters().CountAsync(),
        ["branch_setting"] = await db.BranchSettings.IgnoreQueryFilters().CountAsync(),
        ["branch_service_priority"] = await db.BranchServicePriorities.IgnoreQueryFilters().CountAsync(),
        ["zone"] = await db.Zones.IgnoreQueryFilters().CountAsync(),
        ["counter"] = await db.Counters.IgnoreQueryFilters().CountAsync(),
        ["counter_service_priority"] = await db.CounterServicePriorities.IgnoreQueryFilters().CountAsync(),
        ["device"] = await db.Devices.IgnoreQueryFilters().CountAsync(),
        ["dispenser_setting"] = await db.DispenserSettings.IgnoreQueryFilters().CountAsync(),
        ["dispenser_zone_service"] = await db.DispenserZoneServices.IgnoreQueryFilters().CountAsync(),
        ["ticket_template"] = await db.TicketTemplates.IgnoreQueryFilters().CountAsync(),
    };

    // Seed ids are derived from the numbers in the spec; these helpers read them back.
    private static string ServiceName(Guid id) => $"Service {Enumerable.Range(1, 6).First(n => SeedIds.Service(n) == id)}";

    private static string PriorityName(Guid id) => id == SeedIds.Vip ? "V" : id == SeedIds.Normal ? "N" : "?";

    private static int CounterNumber(Guid id) => Enumerable.Range(1, 4).First(n => SeedIds.Counter(n) == id);

    private static int ZoneNumber(Guid id) => Enumerable.Range(1, 2).First(n => SeedIds.Zone(n) == id);

    private static int DispenserNumber(Guid id) => Enumerable.Range(1, 2).First(n => SeedIds.Dispenser(n) == id);
}

internal sealed class SeedHandle(QmsDbContext db) : IAsyncDisposable
{
    public QmsDbContext Db { get; } = db;

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
