using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Qms.Infrastructure.Persistence;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Auth;

// STORY-003 TASK-004: AC-1..AC-7 against the real API host and PostgreSQL.
[Collection(PostgresCollection.Name)]
public sealed class AuthEndpointsTests(PostgresFixture postgres)
{
    private const string Login = "/api/v1/auth/login";
    private const string Refresh = "/api/v1/auth/refresh";
    private const string Logout = "/api/v1/auth/logout";
    private const string Me = "/api/v1/auth/me";

    // ---- AC-1

    [Fact]
    public async Task Ac1_login_returns_a_15_minute_access_token_with_the_claims_and_a_refresh_cookie()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.LoginCompanyAdminAsync();

        reply.Status.ShouldBe(200);
        reply.Response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        reply.Json.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        reply.Json.GetProperty("expiresIn").GetInt32().ShouldBe(900);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(reply.Json.GetProperty("accessToken").GetString());
        token.Subject.ShouldBe((await host.SeededUserIdAsync(TestAccounts.CompanyAdminUserName)).ToString());
        token.GetClaim("company_id").Value.ShouldBe(SeedCompany1());
        token.Claims.Any(c => c.Type == "role").ShouldBeFalse();
        token.GetClaim("lang").Value.ShouldBe("en");
        token.GetClaim("name").Value.ShouldNotBeNullOrEmpty();
        (token.ValidTo - token.ValidFrom).ShouldBe(TimeSpan.FromMinutes(15));
        token.Alg.ShouldBe("HS256");

        var cookie = reply.RefreshSetCookie.ShouldNotBeNull();
        var attributes = cookie.Split(';').Select(a => a.Trim()).ToList();
        attributes.ShouldContain("httponly");
        attributes.ShouldContain("secure");
        attributes.ShouldContain("samesite=strict");
        attributes.ShouldContain("path=/api/v1/auth");
        attributes.ShouldContain(a => a.StartsWith("expires=", StringComparison.Ordinal));
        reply.RefreshToken!.Length.ShouldBe(43); // 256 bits, base64url
        reply.Body.ShouldNotContain(reply.RefreshToken);
    }

    [Fact]
    public async Task Ac1_the_super_admin_token_has_no_company_claim()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.LoginSuperAdminAsync();

        reply.Status.ShouldBe(200);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(reply.Json.GetProperty("accessToken").GetString());
        token.Claims.Any(c => c.Type == "company_id").ShouldBeFalse();
        token.GetClaim("role").Value.ShouldBe("SuperAdmin");
        reply.Json.GetProperty("user").GetProperty("companyId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Ac1_only_the_hash_of_the_refresh_token_is_stored()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.LoginCompanyAdminAsync();

        await using var db = host.OpenContext();
        var row = await db.RefreshTokens.SingleAsync();
        row.TokenHash.ShouldNotBe(reply.RefreshToken);
        row.TokenHash.Length.ShouldBe(64);
        row.FamilyId.ShouldNotBe(Guid.Empty);
        row.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Login_with_missing_fields_is_a_validation_error()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.SendAsync(HttpMethod.Post, Login, new { userName = "", password = "" });

        reply.Status.ShouldBe(400);
        reply.Code.ShouldBe("validation.failed");
        reply.Json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["userName", "password"], ignoreOrder: true);
    }

    // ---- AC-2

    [Fact]
    public async Task Ac2_wrong_password_and_unknown_user_give_the_same_401()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var wrongPassword = await host.LoginAsync(TestAccounts.CompanyAdminUserName, "Wrong-Passw0rd!");
        var unknownUser = await host.LoginAsync("nobody.here", "Wrong-Passw0rd!");

        foreach (var reply in new[] { wrongPassword, unknownUser })
        {
            reply.Status.ShouldBe(401);
            reply.Code.ShouldBe("auth.invalid_credentials");
            reply.RefreshSetCookie.ShouldBeNull();
        }

        // Same shape, same text: nothing tells the two apart except traceId / correlationId.
        string Stable(ApiReply r) => JsonSerializer.Serialize(r.Json.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "correlationId")).ToDictionary(p => p.Name, p => p.Value.ToString()));
        Stable(wrongPassword).ShouldBe(Stable(unknownUser));
    }

    [Fact]
    public async Task Ac2_an_unknown_user_costs_a_password_verification_like_a_wrong_password()
    {
        using var host = await AuthHost.StartAsync(postgres);
        await host.LoginAsync("warm.up", "x"); // JIT and decoy hash are built once

        var known = await TimeAsync(() => host.LoginAsync(TestAccounts.CompanyAdminUserName, "Wrong-Passw0rd!"));
        var unknown = await TimeAsync(() => host.LoginAsync("nobody.here", "Wrong-Passw0rd!"));

        // Both include one PBKDF2 verification (tens of ms). Without the decoy the unknown user would be near zero.
        unknown.ShouldBeGreaterThan(known * 0.4);
    }

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await action();
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return best;
    }

    // ---- AC-3

    [Fact]
    public async Task Ac3_five_failures_lock_the_user_even_for_the_correct_password()
    {
        using var host = await AuthHost.StartAsync(postgres);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failed = await host.LoginAsync(TestAccounts.CompanyAdminUserName, "Wrong-Passw0rd!");
            failed.Code.ShouldBe("auth.invalid_credentials");
        }

        var locked = await host.LoginCompanyAdminAsync();

        locked.Status.ShouldBe(401);
        locked.Code.ShouldBe("auth.locked_out");
        locked.RefreshSetCookie.ShouldBeNull();
    }

    [Fact]
    public async Task Ac3_a_successful_login_resets_the_failure_count()
    {
        using var host = await AuthHost.StartAsync(postgres);

        for (var round = 0; round < 2; round++)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                (await host.LoginAsync(TestAccounts.CompanyAdminUserName, "Wrong-Passw0rd!")).Code.ShouldBe("auth.invalid_credentials");
            }

            (await host.LoginCompanyAdminAsync()).Status.ShouldBe(200);
        }
    }

    [Fact]
    public async Task Ac3_the_lockout_lasts_15_minutes()
    {
        using var host = await AuthHost.StartAsync(postgres);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await host.LoginAsync(TestAccounts.CompanyAdminUserName, "Wrong-Passw0rd!");
        }

        await using var db = host.OpenContext();
        var user = await db.Users.SingleAsync(u => u.UserName == TestAccounts.CompanyAdminUserName);
        var remaining = user.LockoutEnd!.Value - DateTimeOffset.UtcNow;
        remaining.ShouldBeGreaterThan(TimeSpan.FromMinutes(14));
        remaining.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Login_is_rate_limited_per_address_after_10_attempts_a_minute()
    {
        using var host = await AuthHost.StartAsync(postgres);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            (await host.LoginAsync("nobody.here", "x")).Status.ShouldBe(401);
        }

        var limited = await host.LoginAsync("nobody.here", "x");

        limited.Status.ShouldBe(429);
        limited.Code.ShouldBe("request.rate_limited");
        limited.Response.Headers.Contains("Retry-After").ShouldBeTrue();
    }

    // ---- AC-4

    [Fact]
    public async Task Ac4_refresh_rotates_the_token_and_marks_the_old_one_replaced()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();
        var r1 = login.RefreshToken!;

        var refreshed = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r1);

        refreshed.Status.ShouldBe(200);
        var r2 = refreshed.RefreshToken!;
        r2.ShouldNotBe(r1);
        refreshed.Json.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        refreshed.RefreshSetCookie!.ShouldContain("httponly", Case.Insensitive);

        await using var db = host.OpenContext();
        var rows = await db.RefreshTokens.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        var old = rows.Single(r => r.TokenHash == Qms.Infrastructure.Identity.RefreshTokenHasher.Hash(r1));
        var next = rows.Single(r => r.TokenHash == Qms.Infrastructure.Identity.RefreshTokenHasher.Hash(r2));
        old.ReplacedById.ShouldBe(next.Id);
        old.RevokedAt.ShouldNotBeNull();
        next.FamilyId.ShouldBe(old.FamilyId);
        next.ReplacedById.ShouldBeNull();
    }

    [Fact]
    public async Task Ac4_the_new_access_token_works()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();

        var refreshed = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: login.RefreshToken);
        var me = await host.SendAsync(HttpMethod.Get, Me, bearer: AuthHost.AccessToken(refreshed));

        me.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_or_with_an_unknown_token_is_401_and_clears_the_cookie()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var none = await host.SendAsync(HttpMethod.Post, Refresh);
        var unknown = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: "not-a-real-token");

        foreach (var reply in new[] { none, unknown })
        {
            reply.Status.ShouldBe(401);
            reply.Code.ShouldBe("auth.refresh_invalid");
            reply.RefreshSetCookie!.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        }
    }

    [Fact]
    public async Task Refresh_with_an_expired_token_is_401_refresh_invalid()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();
        await using (var db = host.OpenContext())
        {
            await db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var reply = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: login.RefreshToken);

        reply.Status.ShouldBe(401);
        reply.Code.ShouldBe("auth.refresh_invalid");
    }

    [Fact]
    public async Task Refresh_for_a_deactivated_user_is_401_and_ends_the_family()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();
        await using (var db = host.OpenContext())
        {
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var reply = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: login.RefreshToken);

        reply.Status.ShouldBe(401);
        reply.Code.ShouldBe("auth.refresh_invalid");
        await using var check = host.OpenContext();
        (await check.RefreshTokens.AllAsync(r => r.RevokedAt != null)).ShouldBeTrue();
    }

    // ---- AC-5

    [Fact]
    public async Task Ac5_reusing_a_rotated_token_is_401_and_the_new_token_stops_working_too()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();
        var r1 = login.RefreshToken!;
        var r2 = (await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r1)).RefreshToken!;

        var reused = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r1);
        var second = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r2);

        reused.Status.ShouldBe(401);
        reused.Code.ShouldBe("auth.refresh_reused");
        second.Status.ShouldBe(401);
        second.Code.ShouldBe("auth.refresh_reused");
        await using var db = host.OpenContext();
        (await db.RefreshTokens.AllAsync(r => r.RevokedAt != null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Ac5_reuse_in_one_family_does_not_end_another_session_of_the_same_user()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var laptop = (await host.LoginCompanyAdminAsync()).RefreshToken!;
        var phone = (await host.LoginCompanyAdminAsync()).RefreshToken!;
        await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: laptop);
        (await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: laptop)).Code.ShouldBe("auth.refresh_reused");

        var phoneRefresh = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: phone);

        phoneRefresh.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Ac5_two_concurrent_refreshes_with_the_same_token_let_only_one_win()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var r1 = (await host.LoginCompanyAdminAsync()).RefreshToken!;

        var replies = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r1)));

        replies.Count(r => r.Status == 200).ShouldBeLessThanOrEqualTo(1);
        replies.Where(r => r.Status != 200).ShouldAllBe(r => r.Status == 401);
    }

    // ---- AC-6

    [Fact]
    public async Task Ac6_me_needs_a_valid_access_token()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var anonymous = await host.SendAsync(HttpMethod.Get, Me);
        var garbage = await host.SendAsync(HttpMethod.Get, Me, bearer: "not.a.jwt");

        anonymous.Status.ShouldBe(401);
        anonymous.Code.ShouldBe("auth.unauthorized");
        garbage.Status.ShouldBe(401);
    }

    [Fact]
    public async Task Ac6_an_expired_token_is_401()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var user = AuthHost.UserOf(AuthHost.Company1);
        var expired = host.MintToken(user, new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));
        // Inside the 30 s clock skew a just-expired token is still accepted; 1 hour old is not.
        var withinSkew = host.MintToken(user, new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-15).AddSeconds(-10)));

        (await host.SendAsync(HttpMethod.Get, "/api/v1/test/protected", bearer: expired)).Status.ShouldBe(401);
        (await host.SendAsync(HttpMethod.Get, "/api/v1/test/protected", bearer: withinSkew)).Status.ShouldBe(200);
    }

    [Fact]
    public async Task Ac6_me_returns_company_role_language_and_assignments()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();

        var me = await host.SendAsync(HttpMethod.Get, Me, bearer: AuthHost.AccessToken(login));

        me.Status.ShouldBe(200);
        me.Json.GetProperty("companyId").GetString().ShouldBe(SeedCompany1());
        me.Json.TryGetProperty("role", out _).ShouldBeFalse();
        me.Json.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Company Admin"]);
        me.Json.GetProperty("language").GetString().ShouldBe("en");
        me.Json.GetProperty("userName").GetString().ShouldBe(TestAccounts.CompanyAdminUserName);
        me.Json.GetProperty("branchIds").GetArrayLength().ShouldBe(0);
        me.Json.GetProperty("counterIds").GetArrayLength().ShouldBe(0);
        me.Body.ShouldNotContain("password", Case.Insensitive);
    }

    [Fact]
    public async Task Login_and_me_list_the_branch_and_counter_assignments_of_a_Counter_Staff_user()
    {
        // Sign-in has no company yet, so the assignment lookup must bypass the tenant filter (IgnoreQueryFilters by user id).
        using var host = await AuthHost.StartAsync(postgres);
        const string staffName = "counter.staff";
        const string staffPassword = "Staff-Test-Passw0rd!";
        using (var scope = host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Qms.Infrastructure.Persistence.Identity.AppUser>>();
            var staff = new Qms.Infrastructure.Persistence.Identity.AppUser
            {
                UserName = staffName, DisplayName = "Staff", CompanyId = AuthHost.Company1, Language = "ar",
            };
            (await users.CreateAsync(staff, staffPassword)).Succeeded.ShouldBeTrue();
            await using var db = host.OpenContext();
            // The company role comes from user_role (ADR-009); only Super Admin is an Identity role.
            var staffRoleId = await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.CompanyId == AuthHost.Company1 && r.SystemCode == Qms.Domain.Enums.SystemRoleCode.CounterStaff)
                .Select(r => r.Id).SingleAsync();
            db.CompanyUserRoles.Add(new Qms.Domain.Entities.UserRole { CompanyId = AuthHost.Company1, UserId = staff.Id, RoleId = staffRoleId });
            db.UserBranches.Add(new Qms.Domain.Entities.UserBranch
            {
                CompanyId = AuthHost.Company1, UserId = staff.Id, BranchId = Qms.Infrastructure.Persistence.Seed.SeedIds.Branch1,
            });
            db.UserCounters.Add(new Qms.Domain.Entities.UserCounter
            {
                CompanyId = AuthHost.Company1, UserId = staff.Id, CounterId = Qms.Infrastructure.Persistence.Seed.SeedIds.Counter(2),
            });
            await db.SaveChangesAsync();
        }

        var login = await host.LoginAsync(staffName, staffPassword);
        var me = await host.SendAsync(HttpMethod.Get, Me, bearer: AuthHost.AccessToken(login));

        foreach (var user in new[] { login.Json.GetProperty("user"), me.Json })
        {
            user.TryGetProperty("role", out _).ShouldBeFalse();
            user.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Counter Staff"]);
            user.GetProperty("permissions").GetArrayLength().ShouldBe(0);
            user.GetProperty("language").GetString().ShouldBe("ar");
            user.GetProperty("branchIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([Qms.Infrastructure.Persistence.Seed.SeedIds.Branch1]);
            user.GetProperty("counterIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([Qms.Infrastructure.Persistence.Seed.SeedIds.Counter(2)]);
        }

        new JsonWebTokenHandler().ReadJsonWebToken(AuthHost.AccessToken(login)).GetClaim("lang").Value.ShouldBe("ar");
    }

    // ---- AC-7

    [Fact]
    public async Task Ac7_logout_clears_the_cookie_and_the_old_token_no_longer_refreshes()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();

        var logout = await host.SendAsync(HttpMethod.Post, Logout, refreshCookie: login.RefreshToken);
        var refresh = await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: login.RefreshToken);

        logout.Status.ShouldBe(204);
        logout.RefreshSetCookie!.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        logout.RefreshSetCookie!.ShouldContain("path=/api/v1/auth", Case.Insensitive);
        refresh.Status.ShouldBe(401);
        await using var db = host.OpenContext();
        (await db.RefreshTokens.AllAsync(r => r.RevokedAt != null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Ac7_logout_revokes_the_whole_family_including_the_rotated_successor()
    {
        using var host = await AuthHost.StartAsync(postgres);
        var login = await host.LoginCompanyAdminAsync();
        var r2 = (await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: login.RefreshToken)).RefreshToken!;

        await host.SendAsync(HttpMethod.Post, Logout, refreshCookie: r2);

        (await host.SendAsync(HttpMethod.Post, Refresh, refreshCookie: r2)).Status.ShouldBe(401);
    }

    [Fact]
    public async Task Logout_is_idempotent_and_works_without_a_cookie_or_an_access_token()
    {
        using var host = await AuthHost.StartAsync(postgres);

        var reply = await host.SendAsync(HttpMethod.Post, Logout);

        reply.Status.ShouldBe(204);
    }

    // ---- seeding

    [Fact]
    public async Task Only_SuperAdmin_is_an_identity_role_and_the_two_admins_are_seeded_once()
    {
        using var host = await AuthHost.StartAsync(postgres);

        await using var db = host.OpenContext();
        // Since STORY-048 only SuperAdmin is an Identity role; the company roles are rows of `role` (the Company Admin holds one through user_role).
        (await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync()).ShouldBe(["SuperAdmin"]);
        (await db.CompanyRoles.IgnoreQueryFilters().CountAsync()).ShouldBe(3);
        var users = await db.Users.OrderBy(u => u.UserName).ToListAsync();
        users.Select(u => u.UserName).ShouldBe([TestAccounts.CompanyAdminUserName, TestAccounts.SuperAdminUserName]);
        users.Single(u => u.UserName == TestAccounts.SuperAdminUserName).CompanyId.ShouldBeNull();
        users.Single(u => u.UserName == TestAccounts.CompanyAdminUserName).CompanyId.ShouldBe(AuthHost.Company1);
        users.ShouldAllBe(u => u.PasswordHash != null && !u.PasswordHash.Contains("Passw0rd"));
        (await db.UserBranches.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await db.UserCounters.IgnoreQueryFilters().CountAsync()).ShouldBe(0);

        // A second start changes nothing (idempotent) and does not reset passwords.
        using var second = new QmsApiFactory("Development", connectionString: host.ConnectionString);
        _ = second.Services;
        await using var again = host.OpenContext();
        (await again.Users.CountAsync()).ShouldBe(2);
        (await again.Roles.CountAsync()).ShouldBe(1);
        (await again.CompanyRoles.IgnoreQueryFilters().CountAsync()).ShouldBe(3);
        (await again.CompanyUserRoles.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Users_are_not_seeded_when_the_seed_settings_are_missing()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        using var factory = new QmsApiFactory("Development", connectionString: connectionString, extraConfiguration: new Dictionary<string, string?>
        {
            ["Seed:SuperAdmin:UserName"] = "",
            ["Seed:SuperAdmin:Password"] = "",
            ["Seed:CompanyAdmin:UserName"] = null,
            ["Seed:CompanyAdmin:Password"] = null,
        });
        _ = factory.Services;

        await using var db = PostgresFixture.CreateContext(connectionString);
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.Roles.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Production_does_not_seed_roles_or_users()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        using var factory = new QmsApiFactory("Production", connectionString: connectionString);
        _ = factory.Services;

        await using var db = PostgresFixture.CreateContext(connectionString);
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.Roles.CountAsync()).ShouldBe(0);
    }

    private static string SeedCompany1() => Qms.Infrastructure.Persistence.Seed.SeedIds.Company1.ToString();
}
