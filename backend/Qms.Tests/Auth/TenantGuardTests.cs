using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Common;
using Qms.Domain.Entities;
using InfrastructureRegistration = Qms.Infrastructure.DependencyInjection;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Auth;

// STORY-003 TASK-008: S2 write guard (AC-9, AC-10) on the DbContext, with no HTTP in between.
[Collection(PostgresCollection.Name)]
public sealed class TenantGuardTests(PostgresFixture postgres)
{
    private static Branch NewBranch(Guid companyId, string code = "G1") =>
        new() { CompanyId = companyId, Name = "Guard", Code = code, Timezone = "Asia/Dubai" };

    private async Task<string> SeededAsync() => await postgres.NewDatabaseAsync(seeded: true);

    // ---- AC-9

    [Fact]
    public async Task Ac9_adding_a_branch_with_no_company_and_no_section_throws_NoTenant_and_stores_nothing()
    {
        var connectionString = await SeededAsync();
        await using (var db = PostgresFixture.CreateContext(connectionString, crossTenant: false))
        {
            db.Branches.Add(NewBranch(SeedIds.Company1));
            var exception = await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
            exception.Reason.ShouldBe(TenantViolationReason.NoTenant);
        }

        await using var check = PostgresFixture.CreateContext(connectionString);
        (await check.Branches.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Ac9_modifying_or_deleting_a_counter_with_no_company_and_no_section_throws_NoTenant()
    {
        var connectionString = await SeededAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, crossTenant: false);
        var counter = await db.Counters.IgnoreQueryFilters().SingleAsync(c => c.Id == SeedIds.Counter(1));

        counter.Name = "Changed";
        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.NoTenant);

        db.ChangeTracker.Clear();
        db.Counters.Remove(await db.Counters.IgnoreQueryFilters().SingleAsync(c => c.Id == SeedIds.Counter(4)));
        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.NoTenant);

        await using var check = PostgresFixture.CreateContext(connectionString);
        (await check.Counters.IgnoreQueryFilters().SingleAsync(c => c.Id == SeedIds.Counter(1))).Name.ShouldNotBe("Changed");
        (await check.Counters.IgnoreQueryFilters().AnyAsync(c => c.Id == SeedIds.Counter(4))).ShouldBeTrue();
    }

    [Fact]
    public async Task Ac9_a_Company_row_with_no_company_and_no_section_throws_NoTenant()
    {
        await using var db = PostgresFixture.CreateContext(await SeededAsync(), crossTenant: false);
        db.Companies.Add(new Company { Name = "C", Code = "ZZ", CurrencyCode = "AED" });

        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.NoTenant);
    }

    [Fact]
    public async Task Ac9_identity_and_refresh_token_rows_still_save_with_no_company_and_no_section()
    {
        var connectionString = await SeededAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, crossTenant: false);
        var user = new AppUser { UserName = "plain.user", NormalizedUserName = "PLAIN.USER", DisplayName = "Plain", CompanyId = SeedIds.Company1 };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = new string('a', 64), FamilyId = Guid.NewGuid(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });

        (await db.SaveChangesAsync()).ShouldBe(2);

        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15); // what the lockout does
        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Reads_with_no_company_are_not_blocked_by_the_guard()
    {
        await using var db = PostgresFixture.CreateContext(await SeededAsync(), crossTenant: false);

        (await db.Branches.CountAsync()).ShouldBe(0);
        (await db.Branches.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task With_a_company_set_a_branch_is_stamped_and_another_company_is_refused_with_OtherCompany()
    {
        var connectionString = await SeededAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, SeedIds.Company1);
        db.Branches.Add(NewBranch(Guid.Empty, "STAMP"));
        await db.SaveChangesAsync();
        (await db.Branches.SingleAsync(b => b.Code == "STAMP")).CompanyId.ShouldBe(SeedIds.Company1);

        db.Branches.Add(NewBranch(Guid.NewGuid(), "FOREIGN"));
        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.OtherCompany);
    }

    [Fact]
    public async Task Changing_the_company_of_a_row_throws_CompanyChanged()
    {
        await using var db = PostgresFixture.CreateContext(await SeededAsync(), SeedIds.Company1);
        (await db.Branches.SingleAsync()).CompanyId = Guid.NewGuid();

        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.CompanyChanged);
    }

    // ---- AC-10

    [Fact]
    public async Task Ac10_the_seeder_run_by_MigrateAndSeedAsync_creates_Company_1_and_a_second_run_changes_nothing()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);

        // The host does not seed in Production; run the seed explicitly, the way Development start-up does.
        await InfrastructureRegistration.MigrateAndSeedAsync(factory.Services, CancellationToken.None);
        var first = await CountsAsync(connectionString);
        await InfrastructureRegistration.MigrateAndSeedAsync(factory.Services, CancellationToken.None);
        var second = await CountsAsync(connectionString);

        first.ShouldBe(second);
        first.Companies.ShouldBe(1);
        first.Branches.ShouldBe(1);
        first.Zones.ShouldBe(2);
        first.Counters.ShouldBe(4);
        first.CounterMappings.ShouldBe(7);
    }

    private static async Task<(int Companies, int Branches, int Zones, int Counters, int CounterMappings)> CountsAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return (
            await db.Companies.IgnoreQueryFilters().CountAsync(),
            await db.Branches.IgnoreQueryFilters().CountAsync(),
            await db.Zones.IgnoreQueryFilters().CountAsync(),
            await db.Counters.IgnoreQueryFilters().CountAsync(),
            await db.CounterServicePriorities.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Ac10_inside_a_cross_tenant_section_a_tenant_row_with_an_empty_company_is_rejected()
    {
        var connectionString = await SeededAsync();
        var tenant = new SettableCurrentTenant();
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connectionString);
        await using var db = new QmsDbContext(builder.Options, tenant);

        using (tenant.BeginCrossTenant())
        {
            db.Branches.Add(NewBranch(SeedIds.Company1, "OK"));
            await db.SaveChangesAsync(); // explicit company: accepted

            db.Branches.Add(NewBranch(Guid.Empty, "EMPTY"));
            (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.NoTenant);
        }
    }

    [Fact]
    public async Task Ac10_a_company_change_is_rejected_even_inside_a_cross_tenant_section()
    {
        await using var db = PostgresFixture.CreateContext(await SeededAsync(), crossTenant: true);
        (await db.Branches.IgnoreQueryFilters().SingleAsync()).CompanyId = Guid.NewGuid();

        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.CompanyChanged);
    }

    [Fact]
    public async Task The_seeder_scope_is_closed_after_MigrateAndSeedAsync()
    {
        var connectionString = await postgres.NewBlankDatabaseAsync();
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);
        await InfrastructureRegistration.MigrateAndSeedAsync(factory.Services, CancellationToken.None);

        // A fresh scope (a request) never inherits the seeder's section.
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().IsCrossTenant.ShouldBeFalse();
        var db = scope.ServiceProvider.GetRequiredService<QmsDbContext>();
        db.Branches.Add(NewBranch(SeedIds.Company1, "AFTER"));
        (await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync())).Reason.ShouldBe(TenantViolationReason.NoTenant);
    }
}
