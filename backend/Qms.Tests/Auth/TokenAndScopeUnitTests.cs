using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Qms.Api.Auth;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Infrastructure.Identity;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Auth;

// Unit tests for token creation and hashing (TASK-004) and the nest-counted cross-tenant scope (AC-12).
public sealed class TokenAndScopeUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private static JwtTokenService NewService(DateTimeOffset? now = null, Action<JwtOptions>? configure = null)
    {
        var options = new JwtOptions { Issuer = "qms-test", Audience = "qms-clients", SigningKey = TestAccounts.JwtSigningKey };
        configure?.Invoke(options);
        return new JwtTokenService(Options.Create(options), new FixedTimeProvider(now ?? Now));
    }

    private static CurrentUserDto User(Guid? companyId, bool isSuperAdmin = false) =>
        new(Guid.Parse("11111111-1111-7111-8111-111111111111"), "admin", "Ada Admin", companyId, "ar", isSuperAdmin,
            [], ["service-types.view"], isSuperAdmin ? null : Qms.Domain.Enums.RoleBranchScope.Company, [], []);

    // ---- access token

    [Fact]
    public void The_access_token_carries_sub_company_id_name_and_lang_and_no_role_or_permission_and_expires_in_15_minutes()
    {
        var company = Guid.NewGuid();

        var token = NewService().CreateAccessToken(User(company));

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);
        jwt.Subject.ShouldBe("11111111-1111-7111-8111-111111111111");
        jwt.GetClaim("company_id").Value.ShouldBe(company.ToString());
        jwt.Claims.Any(claim => claim.Type == "role").ShouldBeFalse();
        jwt.Claims.Any(claim => claim.Type is "permissions" or "permission" or "roles").ShouldBeFalse();
        jwt.GetClaim("name").Value.ShouldBe("Ada Admin");
        jwt.GetClaim("lang").Value.ShouldBe("ar");
        jwt.Issuer.ShouldBe("qms-test");
        jwt.Audiences.ShouldBe(["qms-clients"]);
        jwt.Alg.ShouldBe("HS256");
        jwt.ValidFrom.ShouldBe(Now.UtcDateTime);
        jwt.ValidTo.ShouldBe(Now.AddMinutes(15).UtcDateTime);
        token.ExpiresAt.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public void A_Super_Admin_token_has_no_company_id_claim()
    {
        var token = NewService().CreateAccessToken(User(null, isSuperAdmin: true));

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);
        jwt.Claims.Any(claim => claim.Type == "company_id").ShouldBeFalse();
        jwt.GetClaim("role").Value.ShouldBe("SuperAdmin");
    }

    [Fact]
    public async Task The_token_signature_validates_only_with_the_signing_key()
    {
        var token = NewService().CreateAccessToken(User(Guid.NewGuid())).Value;
        var handler = new JsonWebTokenHandler();
        Microsoft.IdentityModel.Tokens.TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = "qms-test",
            ValidAudience = "qms-clients",
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = false,
        };

        (await handler.ValidateTokenAsync(token, Parameters(TestAccounts.JwtSigningKey))).IsValid.ShouldBeTrue();
        (await handler.ValidateTokenAsync(token, Parameters(TestAccounts.JwtSigningKey + "x"))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Two_tokens_for_the_same_user_are_different_strings_only_by_time_not_by_secret_state()
    {
        var first = NewService(Now).CreateAccessToken(User(null, isSuperAdmin: true)).Value;
        var later = NewService(Now.AddSeconds(1)).CreateAccessToken(User(null, isSuperAdmin: true)).Value;

        first.ShouldNotBe(later);
    }

    // ---- options

    [Fact]
    public void Jwt_options_demand_issuer_audience_and_a_256_bit_key()
    {
        string? Validate(Action<JwtOptions> change)
        {
            var options = new JwtOptions { Issuer = "i", Audience = "a", SigningKey = new string('k', 32) };
            change(options);
            return JwtOptions.Validate(options);
        }

        Validate(_ => { }).ShouldBeNull();
        Validate(o => o.SigningKey = "")!.ShouldContain("Jwt:SigningKey");
        Validate(o => o.SigningKey = new string('k', 31))!.ShouldContain("256 bits");
        Validate(o => o.Issuer = " ")!.ShouldContain("Jwt:Issuer");
        Validate(o => o.Audience = "")!.ShouldContain("Jwt:Audience");
        Validate(o => o.AccessTokenMinutes = 0)!.ShouldContain("AccessTokenMinutes");
        Validate(o => o.RefreshTokenDays = 99)!.ShouldContain("RefreshTokenDays");
    }

    [Fact]
    public void A_host_without_a_signing_key_fails_to_start_and_names_the_setting()
    {
        using var factory = new QmsApiFactory("Production", extraConfiguration: new Dictionary<string, string?> { ["Jwt:SigningKey"] = "" });

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
        exception.Message.ShouldContain("Jwt:SigningKey");
        exception.Message.ShouldContain("Jwt__SigningKey");
    }

    // ---- refresh token hashing

    [Fact]
    public void A_refresh_token_is_256_random_bits_in_base64url_and_never_repeats()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RefreshTokenHasher.NewToken()).ToList();

        tokens.Distinct().Count().ShouldBe(200);
        tokens.ShouldAllBe(token => token.Length == 43 && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'));
    }

    [Fact]
    public void The_hash_is_lowercase_hex_sha256()
    {
        // SHA-256("abc")
        RefreshTokenHasher.Hash("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        RefreshTokenHasher.Hash("abc").ShouldBe(RefreshTokenHasher.Hash("abc"));
        RefreshTokenHasher.Hash("abc").ShouldNotBe(RefreshTokenHasher.Hash("abd"));
    }

    [Fact]
    public void Hashes_compare_equal_only_when_identical()
    {
        var hash = RefreshTokenHasher.Hash("abc");

        RefreshTokenHasher.HashesEqual(hash, RefreshTokenHasher.Hash("abc")).ShouldBeTrue();
        RefreshTokenHasher.HashesEqual(hash, RefreshTokenHasher.Hash("abd")).ShouldBeFalse();
        RefreshTokenHasher.HashesEqual(hash, "short").ShouldBeFalse();
    }

    // ---- ICurrentUser

    private static HttpContextCurrentUser UserFrom(params Claim[] claims)
    {
        var identity = claims.Length == 0 ? new ClaimsIdentity() : new ClaimsIdentity(claims, "test", "name", "role");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return new HttpContextCurrentUser(accessor);
    }

    [Fact]
    public void The_current_user_is_read_from_the_claims()
    {
        var id = Guid.NewGuid();
        var company = Guid.NewGuid();

        var user = UserFrom(new Claim("sub", id.ToString()), new Claim("company_id", company.ToString()), new Claim("role", "CounterStaff"), new Claim("lang", "ar"));

        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldBe(id);
        user.CompanyId.ShouldBe(company);
        user.IsSuperAdmin.ShouldBeFalse();
        user.Language.ShouldBe("ar");
    }

    [Fact]
    public void Only_the_SuperAdmin_role_claim_makes_a_Super_Admin()
    {
        UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "SuperAdmin")).IsSuperAdmin.ShouldBeTrue();
        UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "superadmin")).IsSuperAdmin.ShouldBeFalse();
        UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "CompanyAdmin")).IsSuperAdmin.ShouldBeFalse();
        UserFrom(new Claim("sub", Guid.NewGuid().ToString())).IsSuperAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Nobody_is_signed_in_without_a_principal_or_outside_a_request()
    {
        var anonymous = UserFrom();
        var noRequest = new HttpContextCurrentUser(new HttpContextAccessor());

        foreach (var user in new[] { anonymous, noRequest })
        {
            user.IsAuthenticated.ShouldBeFalse();
            user.UserId.ShouldBeNull();
            user.CompanyId.ShouldBeNull();
            user.IsSuperAdmin.ShouldBeFalse();
        }
    }

    [Fact]
    public void A_malformed_company_id_claim_means_no_company_so_the_tenant_fails_closed()
    {
        UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("company_id", "not-a-guid"))
            .CompanyId.ShouldBeNull();
    }

    // ---- ITenantScope nesting (AC-12, unit)

    [Fact]
    public void Disposing_an_inner_handle_does_not_end_the_outer_section()
    {
        var context = new RequestTenantContext(UserFrom());
        context.IsCrossTenant.ShouldBeFalse();

        var outer = context.BeginCrossTenant();
        var inner = context.BeginCrossTenant();
        context.IsCrossTenant.ShouldBeTrue();

        inner.Dispose();
        context.IsCrossTenant.ShouldBeTrue();

        inner.Dispose(); // disposing twice must not end the outer one either
        context.IsCrossTenant.ShouldBeTrue();

        outer.Dispose();
        context.IsCrossTenant.ShouldBeFalse();
    }

    [Fact]
    public void The_request_tenant_company_comes_from_the_user_and_is_null_for_a_Super_Admin()
    {
        var company = Guid.NewGuid();

        new RequestTenantContext(UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("company_id", company.ToString())))
            .CompanyId.ShouldBe(company);
        new RequestTenantContext(UserFrom(new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "SuperAdmin")))
            .CompanyId.ShouldBeNull();
        new RequestTenantContext(new HttpContextCurrentUser(new HttpContextAccessor())).CompanyId.ShouldBeNull();
    }

    [Fact]
    public void Opening_a_section_does_not_give_the_context_a_company()
    {
        var context = new RequestTenantContext(UserFrom());

        using (context.BeginCrossTenant())
        {
            context.CompanyId.ShouldBeNull();
        }
    }
}
