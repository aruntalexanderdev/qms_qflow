using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-002 AC-7 (tenant filter, ADR-003) and the auditable interceptor.
[Collection(PostgresCollection.Name)]
public sealed class TenantAndAuditTests(PostgresFixture postgres)
{
    private static readonly Guid Company2Branch = Guid.CreateVersion7();

    /// <summary>Seeded database plus a second company with a service type, a priority type, a branch and a zone.</summary>
    private async Task<(string ConnectionString, Guid Company2)> TwoCompaniesAsync()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var company2 = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
        db.Companies.Add(company2);
        db.ServiceTypes.Add(new ServiceType { CompanyId = company2.Id, NameEn = "Other service", NameAr = "خدمة أخرى", Prefix = "A" });
        db.PriorityTypes.Add(new PriorityType { CompanyId = company2.Id, NameEn = "Normal", NameAr = "عادي", Prefix = "N", PriorityLevel = 2 });
        db.Branches.Add(new Branch { Id = Company2Branch, CompanyId = company2.Id, NameEn = "Other branch", NameAr = "فرع آخر", Code = "X1", Timezone = "Asia/Riyadh" });
        db.Zones.Add(new Zone { CompanyId = company2.Id, BranchId = Company2Branch, NameEn = "Main zone", NameAr = "المنطقة الرئيسية" });
        await db.SaveChangesAsync();
        return (connectionString, company2.Id);
    }

    [Fact]
    public async Task Queries_return_only_the_current_companys_rows()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);

        (await db.Companies.Select(c => c.Id).ToListAsync()).ShouldBe([SeedIds.Company1]);
        (await db.ServiceTypes.CountAsync()).ShouldBe(6);
        (await db.ServiceTypes.AllAsync(s => s.CompanyId == SeedIds.Company1)).ShouldBeTrue();
        (await db.Branches.CountAsync()).ShouldBe(1);
        (await db.Zones.CountAsync()).ShouldBe(2);
        (await db.PriorityTypes.CountAsync()).ShouldBe(2);

        await using var other = PostgresFixture.CreateContext(connectionString, company2);
        (await other.ServiceTypes.Select(s => s.NameEn).ToListAsync()).ShouldBe(["Other service"]);
        (await other.Branches.Select(b => b.Code).ToListAsync()).ShouldBe(["X1"]);
        (await other.Zones.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_company_queries_return_nothing_and_IgnoreQueryFilters_returns_everything()
    {
        var (connectionString, _) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, companyId: null);

        (await db.ServiceTypes.CountAsync()).ShouldBe(0);
        (await db.Companies.CountAsync()).ShouldBe(0);
        (await db.ServiceTypes.IgnoreQueryFilters().CountAsync()).ShouldBe(7);
        (await db.Companies.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task The_current_company_is_stamped_on_new_tenant_rows()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var service = new ServiceType { NameEn = "Service 7", NameAr = "الخدمة 7", Prefix = "H" };

        db.ServiceTypes.Add(service);
        await db.SaveChangesAsync();

        service.CompanyId.ShouldBe(SeedIds.Company1);
    }

    [Fact]
    public async Task Writing_a_row_of_another_company_throws()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        db.ServiceTypes.Add(new ServiceType { CompanyId = company2, NameEn = "Sneaky", NameAr = "خفي", Prefix = "S" });

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Updating_a_row_of_another_company_throws()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var foreign = await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == company2);
        foreign.NameEn = "Hijacked";

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_row_of_another_company_cannot_be_moved_into_the_current_company()
    {
        // Review S1: load with IgnoreQueryFilters, then set CompanyId to the current company. The ORIGINAL company decides.
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var foreign = await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == company2);
        foreign.CompanyId = SeedIds.Company1;

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());

        await using var check = PostgresFixture.CreateContext(connectionString);
        (await check.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == foreign.Id)).CompanyId.ShouldBe(company2);
    }

    [Fact]
    public async Task The_company_of_a_row_of_the_current_company_cannot_be_changed()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var own = await db.ServiceTypes.FirstAsync();
        own.CompanyId = company2;

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_company_of_a_row_cannot_be_changed_even_without_a_current_company()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, companyId: null);
        var row = await db.ServiceTypes.IgnoreQueryFilters().FirstAsync(s => s.CompanyId == SeedIds.Company1);
        row.CompanyId = company2;

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_row_of_another_company_throws()
    {
        var (connectionString, company2) = await TwoCompaniesAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var foreign = await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == company2);
        db.ServiceTypes.Remove(foreign);

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_row_of_the_current_company_can_still_be_updated()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        var own = await db.ServiceTypes.FirstAsync();
        own.NameEn = "Renamed";

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Timestamps_are_set_in_utc_from_the_time_provider()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var created = new DateTimeOffset(2026, 11, 8, 6, 30, 0, TimeSpan.Zero);
        var updated = created.AddHours(2);
        var id = Guid.Empty;

        await using (var db = PostgresFixture.CreateContext(connectionString, timeProvider: new FixedTimeProvider(created)))
        {
            var service = new ServiceType { CompanyId = SeedIds.Company1, NameEn = "Service 7", NameAr = "الخدمة 7", Prefix = "H" };
            db.ServiceTypes.Add(service);
            await db.SaveChangesAsync();
            id = service.Id;
        }

        await using (var db = PostgresFixture.CreateContext(connectionString, timeProvider: new FixedTimeProvider(updated)))
        {
            var service = await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == id);
            service.NameEn = "Renamed";
            await db.SaveChangesAsync();
        }

        await using var check = PostgresFixture.CreateContext(connectionString);
        var saved = await check.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == id);
        saved.CreatedAt.ShouldBe(created);
        saved.UpdatedAt.ShouldBe(updated);
        saved.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void New_entities_get_a_version_7_uuid()
    {
        var id = new ServiceType { NameEn = "x", NameAr = "x", Prefix = "X" }.Id;

        id.Version.ShouldBe(7);
    }
}
