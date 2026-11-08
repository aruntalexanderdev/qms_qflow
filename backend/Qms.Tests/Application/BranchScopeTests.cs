using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Infrastructure.Scope;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Application;

// STORY-005 AC-30 on the class itself: who passes which branch, and one user_branch read per request.
[Collection(PostgresCollection.Name)]
public sealed class BranchScopeTests(PostgresFixture postgres)
{
    private async Task<(string ConnectionString, Guid Branch2, Guid Manager)> StartAsync()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var branch2 = new Branch { CompanyId = SeedIds.Company1, Name = "B2", Code = "B2", Timezone = "Asia/Dubai" };
        var other = new Company { Name = "C2", Code = "C2", CurrencyCode = "AED" };
        db.Branches.Add(branch2);
        db.Companies.Add(other);
        var manager = new AppUser { UserName = "bm", NormalizedUserName = "BM", DisplayName = "BM", CompanyId = SeedIds.Company1 };
        db.Users.Add(manager);
        await db.SaveChangesAsync();
        db.UserBranches.Add(new UserBranch { CompanyId = SeedIds.Company1, UserId = manager.Id, BranchId = SeedIds.Branch1 });
        await db.SaveChangesAsync();
        return (connectionString, branch2.Id, manager.Id);
    }

    private static (BranchScope Scope, SqlCommandLog Log, QmsDbContext Db) Create(string connectionString, FakeUser user)
    {
        var log = new SqlCommandLog();
        var options = new DbContextOptionsBuilder<QmsDbContext>();
        options.UseQmsPostgres(connectionString).AddInterceptors(log);
        var db = new QmsDbContext(options.Options, new SettableCurrentTenant(user.CompanyId));
        return (new BranchScope(db, user), log, db);
    }

    [Fact]
    public async Task A_company_admin_passes_every_branch_of_its_company_and_needs_no_assignments()
    {
        var (connectionString, branch2, _) = await StartAsync();
        var (scope, log, db) = Create(connectionString, new FakeUser(SeedIds.Company1, Roles.CompanyAdmin));
        await using var _ = db;

        (await scope.EnsureAccessAsync(SeedIds.Branch1, default)).IsSuccess.ShouldBeTrue();
        (await scope.EnsureAccessAsync(branch2, default)).IsSuccess.ShouldBeTrue();
        (await scope.AccessibleBranchIdsAsync(default)).ShouldBeNull();
        log.Commands.ShouldNotContain(sql => sql.Contains("user_branch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_branch_manager_passes_assigned_branches_only_and_reads_the_assignments_once()
    {
        var (connectionString, branch2, manager) = await StartAsync();
        var (scope, log, db) = Create(connectionString, new FakeUser(SeedIds.Company1, Roles.BranchManager, id: manager));
        await using var _ = db;

        (await scope.EnsureAccessAsync(SeedIds.Branch1, default)).IsSuccess.ShouldBeTrue();
        var other = await scope.EnsureAccessAsync(branch2, default);
        (await scope.EnsureAccessAsync(SeedIds.Branch1, default)).IsSuccess.ShouldBeTrue();
        var accessible = await scope.AccessibleBranchIdsAsync(default);

        other.Error!.Code.ShouldBe("auth.forbidden");
        other.Error.Type.ShouldBe(Qms.Application.Common.ErrorType.Forbidden);
        accessible.ShouldBe([SeedIds.Branch1]);
        log.Commands.Count(sql => sql.Contains("FROM user_branch", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_branch_and_a_branch_of_another_company_are_both_not_found()
    {
        var (connectionString, _, manager) = await StartAsync();
        Guid otherCompanyBranch;
        await using (var setup = PostgresFixture.CreateContext(connectionString))
        {
            var company = await setup.Companies.IgnoreQueryFilters().SingleAsync(c => c.Code == "C2");
            var branch = new Branch { CompanyId = company.Id, Name = "X", Code = "X1", Timezone = "Asia/Dubai" };
            setup.Branches.Add(branch);
            await setup.SaveChangesAsync();
            otherCompanyBranch = branch.Id;
        }

        foreach (var (role, id) in new[] { (Roles.CompanyAdmin, Guid.NewGuid()), (Roles.BranchManager, manager) })
        {
            var (scope, _, db) = Create(connectionString, new FakeUser(SeedIds.Company1, role, id: id));
            await using var _ = db;

            var foreign = await scope.EnsureAccessAsync(otherCompanyBranch, default);
            var unknown = await scope.EnsureAccessAsync(Guid.NewGuid(), default);

            foreign.Error!.Code.ShouldBe("resource.not_found");
            unknown.Error!.Code.ShouldBe("resource.not_found");
            foreign.Error.Message.ShouldBe(unknown.Error.Message);
        }
    }

    [Fact]
    public async Task A_user_without_a_company_is_forbidden_and_has_no_branches()
    {
        var (connectionString, _, _) = await StartAsync();
        var (scope, _, db) = Create(connectionString, new FakeUser(null, Roles.SuperAdmin));
        await using var _ = db;

        (await scope.EnsureAccessAsync(SeedIds.Branch1, default)).Error!.Code.ShouldBe("auth.forbidden");
        (await scope.AccessibleBranchIdsAsync(default)).ShouldBeEmpty();
    }
}
