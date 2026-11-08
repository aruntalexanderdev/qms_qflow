using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Qms.Application.Auth;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Auth;

// STORY-003: protected by default (AC-6), JWT validation rules (ADR-004), OpenAPI bearer scheme (AC-8).
public sealed class ProtectionTests
{
    private static readonly string[] AnonymousPaths = ["/health/live", "/health/ready", "/openapi/v1.json", "/swagger/index.html"];

    // ---- AC-8

    [Fact]
    public async Task Ac8_the_openapi_document_lists_the_auth_endpoints_and_the_bearer_scheme()
    {
        using var factory = new QmsApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None)).RootElement;
        var paths = document.GetProperty("paths");
        paths.GetProperty("/api/v1/auth/login").TryGetProperty("post", out _).ShouldBeTrue();
        paths.GetProperty("/api/v1/auth/refresh").TryGetProperty("post", out _).ShouldBeTrue();
        paths.GetProperty("/api/v1/auth/logout").TryGetProperty("post", out _).ShouldBeTrue();
        paths.GetProperty("/api/v1/auth/me").TryGetProperty("get", out _).ShouldBeTrue();
        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        scheme.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
        document.GetProperty("security").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Ac8_swagger_ui_is_served_in_development_without_signing_in()
    {
        using var factory = new QmsApiFactory("Development");
        using var client = factory.CreateClient();

        (await client.GetAsync("/swagger/index.html", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- AC-6: protected by default

    [Fact]
    public async Task An_endpoint_without_any_attribute_needs_a_signed_in_user()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync("/api/v1/test/protected", CancellationToken.None);
        var open = await client.GetAsync("/api/v1/test/protected/open", CancellationToken.None);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync(CancellationToken.None)).RootElement
            .GetProperty("code").GetString().ShouldBe("auth.unauthorized");
        open.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_openapi_and_swagger_stay_anonymous()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        foreach (var path in AnonymousPaths.Where(p => p != "/health/ready"))
        {
            (await client.GetAsync(path, CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task An_unknown_route_stays_404_not_401()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/does-not-exist", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- JWT validation

    private static string Mint(
        string issuer = "qms-api",
        string audience = "qms-clients",
        string key = TestAccounts.JwtSigningKey,
        string algorithm = SecurityAlgorithms.HmacSha256,
        DateTime? expires = null,
        string? role = "CompanyAdmin")
    {
        var claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["company_id"] = Guid.NewGuid().ToString() };
        if (role is not null)
        {
            claims["role"] = role;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm),
        });
    }

    [Fact]
    public async Task A_correct_token_is_accepted_and_every_other_kind_is_rejected()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        async Task<HttpStatusCode> CallAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/protected");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return (await client.SendAsync(request, CancellationToken.None)).StatusCode;
        }

        (await CallAsync(Mint())).ShouldBe(HttpStatusCode.OK);
        (await CallAsync(Mint(issuer: "someone-else"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(Mint(audience: "other-app"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(Mint(key: TestAccounts.JwtSigningKey + "-different"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(Mint(algorithm: SecurityAlgorithms.HmacSha384))).ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(Mint(expires: DateTime.UtcNow.AddMinutes(-5)))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unsigned_alg_none_token_is_rejected()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var token = $"{B64("""{"alg":"none","typ":"JWT"}""")}.{B64($$"""{"sub":"{{Guid.NewGuid()}}","role":"SuperAdmin","iss":"qms-api","aud":"qms-clients","exp":{{exp}}}""")}.";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/protected");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        (await client.SendAsync(request, CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- SignalR access_token

    [Fact]
    public async Task The_access_token_query_parameter_is_accepted_only_under_hubs()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();
        var token = Uri.EscapeDataString(Mint());

        (await client.GetAsync($"/hubs/test?access_token={token}", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/hubs/test", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/test/protected?access_token={token}", CancellationToken.None)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- role policies

    [Fact]
    public async Task One_authorization_policy_per_role_exists()
    {
        using var factory = new QmsApiFactory();
        var provider = (Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider)factory.Services
            .GetService(typeof(Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider))!;

        foreach (var role in Roles.All)
        {
            (await provider.GetPolicyAsync(role)).ShouldNotBeNull(role);
        }

        (await provider.GetFallbackPolicyAsync()).ShouldNotBeNull();
    }
}
