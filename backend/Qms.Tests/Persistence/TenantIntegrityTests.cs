using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// Stage 6 review (STORY-002, M1 and M2): a row cannot point at another company's or another branch's data,
// even when its own company_id is "right". The database is the backstop behind the tenant filter.
[Collection(PostgresCollection.Name)]
public sealed class TenantIntegrityTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 11, 8);

    private sealed record World(
        string ConnectionString,
        Guid Company2,
        Guid Branch2,
        Guid Zone2,
        Guid Counter2,
        Guid Device2,
        Guid Token2,
        Guid BranchB,
        Guid ZoneB,
        Guid CounterB,
        Guid Token1);

    /// <summary>
    /// Seeded Company 1 / Branch 1, plus a second branch in Company 1 (zone, counter) and a second company with a
    /// branch, zone, counter, dispenser device (no settings yet) and token. Plus one token in Branch 1.
    /// </summary>
    private async Task<World> WorldAsync()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);

        var company2 = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
        var service2 = new ServiceType { CompanyId = company2.Id, NameEn = "Other service", NameAr = "خدمة أخرى", Prefix = "A" };
        var priority2 = new PriorityType { CompanyId = company2.Id, NameEn = "Normal", NameAr = "عادي", Prefix = "N", PriorityLevel = 2 };
        var branch2 = new Branch { CompanyId = company2.Id, NameEn = "Other branch", NameAr = "فرع آخر", Code = "X1", Timezone = "Asia/Riyadh" };
        var zone2 = new Zone { CompanyId = company2.Id, BranchId = branch2.Id, NameEn = "Main zone", NameAr = "المنطقة الرئيسية" };
        var counter2 = new Counter { CompanyId = company2.Id, BranchId = branch2.Id, ZoneId = zone2.Id, Name = "Counter X", Number = 1 };
        var device2 = new Device { CompanyId = company2.Id, BranchId = branch2.Id, ZoneId = zone2.Id, Type = DeviceType.Dispenser, Name = "Other dispenser" };
        var token2 = new Token
        {
            CompanyId = company2.Id, BranchId = branch2.Id, BusinessDate = Today, Number = "NA0001", PriorityPrefix = "N", ServicePrefix = "A", Sequence = 1,
            PriorityTypeId = priority2.Id, CurrentServiceTypeId = service2.Id, CurrentZoneId = zone2.Id, IssuedAt = DateTimeOffset.UtcNow,
        };

        var branchB = new Branch { CompanyId = SeedIds.Company1, NameEn = "Branch 2", NameAr = "الفرع 2", Code = "B2", Timezone = "Asia/Dubai" };
        var zoneB = new Zone { CompanyId = SeedIds.Company1, BranchId = branchB.Id, NameEn = "Zone B", NameAr = "المنطقة ب" };
        var counterB = new Counter { CompanyId = SeedIds.Company1, BranchId = branchB.Id, ZoneId = zoneB.Id, Name = "Counter B", Number = 1 };
        var token1 = new Token
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, BusinessDate = Today, Number = "NB0001", PriorityPrefix = "N", ServicePrefix = "B", Sequence = 1,
            PriorityTypeId = SeedIds.Normal, CurrentServiceTypeId = SeedIds.Service(2), CurrentZoneId = SeedIds.Zone(1), IssuedAt = DateTimeOffset.UtcNow,
        };

        db.Companies.Add(company2);
        await db.SaveChangesAsync();
        db.AddRange(service2, priority2, branch2, branchB);
        await db.SaveChangesAsync();
        db.AddRange(zone2, zoneB);
        await db.SaveChangesAsync();
        db.AddRange(counter2, device2, counterB, token1);
        await db.SaveChangesAsync();
        db.Tokens.Add(token2);
        await db.SaveChangesAsync();

        return new World(connectionString, company2.Id, branch2.Id, zone2.Id, counter2.Id, device2.Id, token2.Id, branchB.Id, zoneB.Id, counterB.Id, token1.Id);
    }

    private static TokenVisit Visit(Guid company, Guid branch, Guid token, Guid zone, Guid? counter = null) => new()
    {
        CompanyId = company, BranchId = branch, TokenId = token, ServiceTypeId = SeedIds.Service(2), PriorityTypeId = SeedIds.Normal,
        ZoneId = zone, CounterId = counter, QueuedAt = DateTimeOffset.UtcNow,
    };

    // ---- M1: token_visit

    [Fact]
    public async Task A_visit_in_another_companys_zone_is_rejected()
    {
        // The reviewer's proof (b): a Company 1 token queued in a Company 2 zone.
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, world.Zone2));

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_token_visit_zones_branch_id_zone_id");
    }

    [Fact]
    public async Task A_visit_cannot_be_moved_to_another_companys_branch_by_changing_its_branch_id()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, world.Branch2, world.Token2, world.Zone2));

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_token_visit_branch_company_id_branch_id");
    }

    [Fact]
    public async Task A_visit_in_another_branchs_zone_of_the_same_company_is_rejected()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, world.ZoneB));

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_token_visit_zones_branch_id_zone_id");
    }

    [Fact]
    public async Task A_visit_with_a_counter_of_another_branch_is_rejected()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, SeedIds.Zone(1), world.CounterB));

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_token_visit_counter_branch_id_counter_id");
    }

    [Fact]
    public async Task A_visit_for_the_token_of_another_company_is_rejected()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token2, SeedIds.Zone(1)));

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_token_visit_token_branch_id_token_id");
    }

    [Fact]
    public async Task A_visit_without_a_counter_is_accepted_because_the_nullable_composite_key_is_match_simple()
    {
        // Not yet called: counter_id is null, so (branch_id, counter_id) is not checked (PostgreSQL MATCH SIMPLE).
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, SeedIds.Zone(1)));

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_visit_with_token_zone_and_counter_of_its_own_branch_is_accepted()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, SeedIds.Zone(1), SeedIds.Counter(1)));

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_token_removes_its_visits()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.TokenVisits.Add(Visit(SeedIds.Company1, SeedIds.Branch1, world.Token1, SeedIds.Zone(1)));
        await db.SaveChangesAsync();

        await db.Tokens.IgnoreQueryFilters().Where(t => t.Id == world.Token1).ExecuteDeleteAsync();

        (await db.TokenVisits.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    // ---- M2

    [Fact]
    public async Task A_dispenser_mapping_row_of_one_company_on_another_companys_branch_is_rejected()
    {
        // The reviewer's proof (a): company_id = Company 1 and Company 1's service, but Company 2's branch, device and zone.
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.DispenserZoneServices.Add(new DispenserZoneService
        {
            CompanyId = SeedIds.Company1, BranchId = world.Branch2, DeviceId = world.Device2, ZoneId = world.Zone2, ServiceTypeId = SeedIds.Service(1),
        });

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_dispenser_zone_service_branch_company_id_branch_id");
    }

    [Fact]
    public async Task A_dispenser_setting_of_one_company_on_another_companys_device_is_rejected()
    {
        // The reviewer's proof (c).
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.DispenserSettings.Add(new DispenserSetting { CompanyId = SeedIds.Company1, DeviceId = world.Device2, TicketTemplateId = SeedIds.TicketTemplate1 });

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_dispenser_setting_device_company_id_device_id");
    }

    [Fact]
    public async Task A_dispenser_setting_of_the_devices_own_company_is_accepted_and_removed_with_the_device()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.DispenserSettings.Add(new DispenserSetting { CompanyId = world.Company2, DeviceId = world.Device2 });
        await db.SaveChangesAsync();

        await db.Devices.IgnoreQueryFilters().Where(d => d.Id == world.Device2).ExecuteDeleteAsync();

        (await db.DispenserSettings.IgnoreQueryFilters().AnyAsync(s => s.DeviceId == world.Device2)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_counter_mapping_row_cannot_carry_another_companys_id()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.CounterServicePriorities.Add(new CounterServicePriority
        {
            CompanyId = world.Company2, BranchId = SeedIds.Branch1, CounterId = SeedIds.Counter(2), ServiceTypeId = SeedIds.Service(1), PriorityTypeId = SeedIds.Normal,
        });

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_counter_service_priority_branch_company_id_branch_id");
    }

    [Fact]
    public async Task A_message_log_row_cannot_point_at_another_companys_token()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.MessageLogs.Add(new MessageLog
        {
            CompanyId = SeedIds.Company1, TokenId = world.Token2, Channel = MessageChannel.Sms, TemplateCode = "token_issued", ToPhone = "+971500000000",
        });

        (await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation)).ShouldBe("fk_message_log_tokens_company_id_token_id");
    }

    [Fact]
    public async Task A_message_log_row_for_a_token_of_its_own_company_is_accepted()
    {
        var world = await WorldAsync();
        await using var db = PostgresFixture.CreateContext(world.ConnectionString);
        db.MessageLogs.Add(new MessageLog
        {
            CompanyId = SeedIds.Company1, TokenId = world.Token1, Channel = MessageChannel.Sms, TemplateCode = "token_issued", ToPhone = "+971500000000",
        });

        (await db.SaveChangesAsync()).ShouldBe(1);
    }
}
