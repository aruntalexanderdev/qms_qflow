using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;
using Serilog.Events;

namespace Qms.Tests.Auth;

// STORY-003 TASK-008 through the real host with test-host-only endpoints (Support/TenantTestEndpoints.cs):
// [CrossTenant] (AC-11, AC-12), user directory (AC-17), exception mapping (AC-18..AC-20), claims-based tenant (AC-21, AC-22).
[Collection(PostgresCollection.Name)]
public sealed class CrossTenantAndErrorMappingTests(PostgresFixture postgres)
{
    private const string Base = "/api/v1/test/tenant";

    private static async Task<string> CompanyAdminTokenAsync(AuthHost host) => AuthHost.AccessToken(await host.LoginCompanyAdminAsync());

    private static async Task<string> SuperAdminTokenAsync(AuthHost host) => AuthHost.AccessToken(await host.LoginSuperAdminAsync());

    private static string[] Codes(ApiReply reply) => reply.Json.EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray();

    // ---- AC-11

    [Fact]
    public async Task Ac11_a_Company_Admin_gets_403_from_a_CrossTenant_handler_and_the_body_does_not_run()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await CompanyAdminTokenAsync(host);

        var reply = await host.SendAsync(HttpMethod.Post, Base + "/branches", new { companyId = company2.CompanyId, code = "NOPE" }, token);

        reply.Status.ShouldBe(403);
        reply.Code.ShouldBe("auth.forbidden");
        var spy = host.Factory.Services.GetRequiredService<HandlerSpy>();
        spy.Runs.ShouldBe(0);
        spy.SawCrossTenant.ShouldBeNull();
        await using var db = host.OpenContext();
        (await db.Branches.IgnoreQueryFilters().AnyAsync(b => b.Code == "NOPE")).ShouldBeFalse();
    }

    [Fact]
    public async Task Ac11_an_anonymous_caller_cannot_reach_a_CrossTenant_endpoint_at_all()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.SendAsync(HttpMethod.Get, Base + "/all-branches");

        reply.Status.ShouldBe(401);
        host.Factory.Services.GetRequiredService<HandlerSpy>().Runs.ShouldBe(0);
    }

    [Fact]
    public async Task Ac11_a_Branch_Manager_is_refused_too()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var token = host.MintToken(AuthHost.UserOf(AuthHost.Company1, Qms.Application.Auth.Roles.BranchManager));

        (await host.SendAsync(HttpMethod.Get, Base + "/all-branches", bearer: token)).Status.ShouldBe(403);
    }

    // ---- AC-12

    [Fact]
    public async Task Ac12_a_Super_Admin_creates_a_branch_for_Company_2_and_the_section_closes_afterwards()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await SuperAdminTokenAsync(host);

        var reply = await host.SendAsync(HttpMethod.Post, Base + "/branches-then-tenant-write", new { companyId = company2.CompanyId, code = "SA2" }, token);

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("sectionOpenAfterHandler").GetBoolean().ShouldBeFalse();
        reply.Json.GetProperty("followUp").GetString().ShouldBe("NoTenant"); // the same scope fails as in AC-9
        var spy = host.Factory.Services.GetRequiredService<HandlerSpy>();
        spy.Runs.ShouldBe(1);
        spy.SawCrossTenant.ShouldBe(true);
        await using var db = host.OpenContext();
        var created = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.Code == "SA2");
        created.CompanyId.ShouldBe(company2.CompanyId);
        (await db.Branches.IgnoreQueryFilters().AnyAsync(b => b.Code == "SA2L")).ShouldBeFalse();
    }

    // ---- AC-17

    [Fact]
    public async Task Ac17_a_user_id_of_another_company_looks_exactly_like_an_unknown_id()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await CompanyAdminTokenAsync(host);
        var ownUser = await host.SeededUserIdAsync(TestAccounts.CompanyAdminUserName);

        var foreign = await host.SendAsync(HttpMethod.Get, $"{Base}/users/{company2.UserId}", bearer: token);
        var unknown = await host.SendAsync(HttpMethod.Get, $"{Base}/users/{Guid.NewGuid()}", bearer: token);
        var own = await host.SendAsync(HttpMethod.Get, $"{Base}/users/{ownUser}", bearer: token);

        foreign.Status.ShouldBe(404);
        foreign.Code.ShouldBe("user.not_found");
        unknown.Status.ShouldBe(404);
        unknown.Code.ShouldBe("user.not_found");
        foreign.Json.GetProperty("title").GetString().ShouldBe(unknown.Json.GetProperty("title").GetString());
        foreign.Json.GetProperty("detail").GetString().ShouldBe(unknown.Json.GetProperty("detail").GetString());
        own.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Ac17_IsInCompanyAsync_is_true_only_for_a_user_of_that_company()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        using var scope = host.Factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<Qms.Application.Auth.IUserDirectory>();
        var ownUser = await host.SeededUserIdAsync(TestAccounts.CompanyAdminUserName);
        var superAdmin = await host.SeededUserIdAsync(TestAccounts.SuperAdminUserName);

        (await directory.IsInCompanyAsync(ownUser, AuthHost.Company1, CancellationToken.None)).ShouldBeTrue();
        (await directory.IsInCompanyAsync(company2.UserId, AuthHost.Company1, CancellationToken.None)).ShouldBeFalse();
        (await directory.IsInCompanyAsync(company2.UserId, company2.CompanyId, CancellationToken.None)).ShouldBeTrue();
        (await directory.IsInCompanyAsync(superAdmin, AuthHost.Company1, CancellationToken.None)).ShouldBeFalse();
        (await directory.IsInCompanyAsync(Guid.NewGuid(), AuthHost.Company1, CancellationToken.None)).ShouldBeFalse();
    }

    // ---- AC-18

    [Fact]
    public async Task Ac18_a_concurrency_conflict_is_409_request_conflict()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var token = await CompanyAdminTokenAsync(host);
        var throwaway = Guid.CreateVersion7();
        await using (var db = host.OpenContext())
        {
            db.Counters.Add(new Qms.Domain.Entities.Counter
            {
                Id = throwaway, CompanyId = AuthHost.Company1, BranchId = SeedIds.Branch1, ZoneId = SeedIds.Zone(1), Name = "Race", Number = 9,
            });
            await db.SaveChangesAsync();
        }

        var reply = await host.SendAsync(HttpMethod.Put, $"{Base}/counters/{throwaway}/racing-rename", bearer: token);

        reply.Status.ShouldBe(409);
        reply.Code.ShouldBe("request.conflict");
        reply.Body.ShouldNotContain("DbUpdateConcurrencyException");
    }

    // ---- AC-19

    [Theory]
    [InlineData("update-other-company", "OtherCompany")]
    [InlineData("change-company", "CompanyChanged")]
    public async Task Ac19_a_cross_company_write_is_404_resource_not_found_with_a_warning_and_no_entity_values(string mode, string reason)
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await CompanyAdminTokenAsync(host);
        var targetBranch = mode == "update-other-company" ? company2.BranchId : SeedIds.Branch1;

        var reply = await host.SendAsync(HttpMethod.Post, Base + "/touch", new { mode, branchId = targetBranch, otherCompanyId = company2.CompanyId }, token);

        reply.Status.ShouldBe(404);
        reply.Code.ShouldBe("resource.not_found");
        reply.Body.ShouldNotContain(company2.CompanyId.ToString());
        reply.Body.ShouldNotContain(company2.BranchId.ToString());
        reply.Body.ShouldNotContain("Violation", Case.Insensitive);
        var warnings = await host.Factory.LogSink.WaitForAsync(e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("Tenant violation"));
        warnings.ShouldNotBeEmpty();
        var rendered = warnings.Select(e => e.RenderMessage()).ToList();
        rendered.ShouldAllBe(m => m.Contains(reason) && !m.Contains(company2.CompanyId.ToString()) && !m.Contains(company2.BranchId.ToString()));
        warnings.ShouldAllBe(e => e.Exception == null);
        await using var db = host.OpenContext();
        (await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.Id == SeedIds.Branch1)).CompanyId.ShouldBe(AuthHost.Company1);
    }

    // ---- AC-20

    [Fact]
    public async Task Ac20_a_tenant_write_with_no_company_is_500_without_details_and_an_Error_is_logged()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await SuperAdminTokenAsync(host);

        var reply = await host.SendAsync(HttpMethod.Post, Base + "/touch", new { mode = "no-tenant", branchId = Guid.NewGuid(), otherCompanyId = company2.CompanyId }, token);

        reply.Status.ShouldBe(500);
        reply.Code.ShouldBe("server.unexpected_error");
        reply.Body.ShouldNotContain("TenantViolation");
        reply.Body.ShouldNotContain("Branch");
        reply.Body.ShouldNotContain("InvalidOperationException");
        reply.Body.ShouldNotContain(" at Qms.");
        reply.Body.ShouldNotContain(company2.CompanyId.ToString());
        var errors = await host.Factory.LogSink.WaitForAsync(e => e.Level == LogEventLevel.Error && e.Exception is not null);
        errors.ShouldContain(e => e.Exception!.GetType().Name == "TenantViolationException");
        await using var db = host.OpenContext();
        (await db.Branches.IgnoreQueryFilters().AnyAsync(b => b.Code == "NT")).ShouldBeFalse();
    }

    // ---- AC-21

    [Fact]
    public async Task Ac21_the_tenant_comes_from_the_company_id_claim()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var company1Token = await CompanyAdminTokenAsync(host);
        var company2Token = host.MintToken(AuthHost.UserOf(company2.CompanyId));

        var first = await host.SendAsync(HttpMethod.Get, Base + "/branches", bearer: company1Token);
        var second = await host.SendAsync(HttpMethod.Get, Base + "/branches", bearer: company2Token);

        first.Json.EnumerateArray().Select(e => e.GetProperty("companyId").GetGuid()).Distinct().ShouldBe([AuthHost.Company1]);
        first.Json.GetArrayLength().ShouldBe(1);
        second.Json.EnumerateArray().Select(e => e.GetProperty("companyId").GetGuid()).Distinct().ShouldBe([company2.CompanyId]);
        second.Json.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Ac21_ICurrentTenant_is_registered_by_the_auth_setup_and_NoCompanyCurrentTenant_is_gone()
    {
        typeof(Qms.Api.Common.ProblemDetailsSetup).Assembly.GetType("Qms.Api.Common.NoCompanyCurrentTenant").ShouldBeNull();

        using var health = new QmsApiFactory();
        using var scope = health.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Qms.Application.Common.ICurrentTenant>()
            .ShouldBeOfType<Qms.Api.Auth.RequestTenantContext>();
        scope.ServiceProvider.GetRequiredService<Qms.Application.Common.ITenantScope>()
            .ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<Qms.Application.Common.ICurrentTenant>());

        // Not in AddQmsHealthChecks any more.
        var source = await File.ReadAllTextAsync(Path.Combine(FindApiDirectory(), "Common", "ApplicationBuilderExtensions.cs"));
        source.ShouldNotContain("ICurrentTenant");
    }

    private static string FindApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Qms.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "Qms.Api");
    }

    // ---- AC-22

    [Fact]
    public async Task Ac22_a_Super_Admin_sees_nothing_tenant_scoped_by_default_and_everything_through_a_CrossTenant_handler()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var company2 = await host.AddCompany2Async();
        var token = await SuperAdminTokenAsync(host);

        var plain = await host.SendAsync(HttpMethod.Get, Base + "/branches", bearer: token);
        var crossTenant = await host.SendAsync(HttpMethod.Get, Base + "/all-branches", bearer: token);

        plain.Status.ShouldBe(200);
        plain.Json.GetArrayLength().ShouldBe(0);
        crossTenant.Status.ShouldBe(200);
        crossTenant.Json.EnumerateArray().Select(e => e.GetProperty("companyId").GetGuid())
            .ShouldBe([AuthHost.Company1, company2.CompanyId], ignoreOrder: true);
    }
}
