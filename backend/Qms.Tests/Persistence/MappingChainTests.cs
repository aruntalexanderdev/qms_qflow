using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-002 AC-3 and AC-4: the mapping chain is enforced by composite foreign keys.
[Collection(PostgresCollection.Name)]
public sealed class MappingChainTests(PostgresFixture postgres)
{
    private static readonly Guid Service4 = SeedIds.Service(4);
    private static readonly Guid Service6 = SeedIds.Service(6);

    [Fact]
    public async Task Branch_mapping_row_for_a_pair_missing_from_the_company_mapping_is_rejected()
    {
        // Service 4 VIP is not in the company mapping (spec 4.1).
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));
        db.BranchServicePriorities.Add(new BranchServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, ServiceTypeId = Service4, PriorityTypeId = SeedIds.Vip, MaxTokensPerDay = 100,
        });

        var constraint = await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);

        constraint.ShouldNotBeNull().ShouldContain("company_service_priorit");
    }

    [Fact]
    public async Task Counter_mapping_row_for_a_pair_missing_from_the_branch_mapping_is_rejected()
    {
        // Service 6 Normal is in the company mapping but Branch 1 does not offer it (spec 4.2).
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));
        db.CounterServicePriorities.Add(new CounterServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, CounterId = SeedIds.Counter(1), ServiceTypeId = Service6, PriorityTypeId = SeedIds.Normal,
        });

        var constraint = await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);

        constraint.ShouldNotBeNull().ShouldContain("branch_service_priorit");
    }

    [Fact]
    public async Task Counter_mapping_row_for_a_valid_branch_pair_is_accepted()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));
        db.CounterServicePriorities.Add(new CounterServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, CounterId = SeedIds.Counter(2), ServiceTypeId = SeedIds.Service(1), PriorityTypeId = SeedIds.Normal,
        });

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_pair_set_to_Not_Available_stays_mapped_to_its_counter()
    {
        // Branch 1 - Service 2 - VIP becomes Not Available; Counter 2 keeps its Service 2 VIP row (spec 4.3).
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var pair = await db.BranchServicePriorities.IgnoreQueryFilters()
                .SingleAsync(m => m.ServiceTypeId == SeedIds.Service(2) && m.PriorityTypeId == SeedIds.Vip);
            pair.IsAvailable = false;

            (await db.SaveChangesAsync()).ShouldBe(1);
        }

        await using var check = PostgresFixture.CreateContext(connectionString);
        (await check.CounterServicePriorities.IgnoreQueryFilters()
            .AnyAsync(m => m.CounterId == SeedIds.Counter(2) && m.ServiceTypeId == SeedIds.Service(2) && m.PriorityTypeId == SeedIds.Vip)).ShouldBeTrue();

        // A new counter may also be mapped to the Not Available pair.
        check.CounterServicePriorities.Add(new CounterServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, CounterId = SeedIds.Counter(1), ServiceTypeId = SeedIds.Service(2), PriorityTypeId = SeedIds.Vip,
        });
        (await check.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_branch_pair_that_a_counter_uses_is_rejected()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));
        var pair = await db.BranchServicePriorities.IgnoreQueryFilters()
            .SingleAsync(m => m.ServiceTypeId == SeedIds.Service(3) && m.PriorityTypeId == SeedIds.Vip);
        db.BranchServicePriorities.Remove(pair);

        await db.ShouldBeRejectedAsync(SqlState.RestrictViolation);
    }

    [Fact]
    public async Task Deleting_a_company_pair_that_a_branch_uses_is_rejected()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));
        var pair = await db.CompanyServicePriorities.IgnoreQueryFilters()
            .SingleAsync(m => m.ServiceTypeId == SeedIds.Service(2) && m.PriorityTypeId == SeedIds.Vip);
        db.CompanyServicePriorities.Remove(pair);

        await db.ShouldBeRejectedAsync(SqlState.RestrictViolation);
    }

    [Fact]
    public async Task A_counter_cannot_use_a_zone_of_another_branch()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var otherBranch = new Branch { CompanyId = SeedIds.Company1, Name = "Branch 2", Code = "B2", Timezone = "Asia/Dubai" };
        var otherZone = new Zone { CompanyId = SeedIds.Company1, BranchId = otherBranch.Id, Name = "Zone X" };
        db.AddRange(otherBranch, otherZone);
        await db.SaveChangesAsync();

        db.Counters.Add(new Counter { CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, ZoneId = otherZone.Id, Name = "Counter 9", Number = 9 });

        await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_branch_mapping_row_cannot_carry_another_companys_id()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var other = new Company { Name = "Company 2", Code = "C2", CurrencyCode = "AED" };
        db.Companies.Add(other);
        await db.SaveChangesAsync();

        db.BranchServicePriorities.Add(new BranchServicePriority
        {
            CompanyId = other.Id, BranchId = SeedIds.Branch1, ServiceTypeId = SeedIds.Service(6), PriorityTypeId = SeedIds.Normal, MaxTokensPerDay = 10,
        });

        await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_dispenser_mapping_row_cannot_use_a_zone_of_another_branch()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var otherBranch = new Branch { CompanyId = SeedIds.Company1, Name = "Branch 2", Code = "B2", Timezone = "Asia/Dubai" };
        var otherZone = new Zone { CompanyId = SeedIds.Company1, BranchId = otherBranch.Id, Name = "Zone X" };
        db.AddRange(otherBranch, otherZone);
        await db.SaveChangesAsync();

        db.DispenserZoneServices.Add(new DispenserZoneService
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, DeviceId = SeedIds.Dispenser(1), ZoneId = otherZone.Id, ServiceTypeId = SeedIds.Service(1),
        });

        await db.ShouldBeRejectedAsync(SqlState.ForeignKeyViolation);
    }
}
