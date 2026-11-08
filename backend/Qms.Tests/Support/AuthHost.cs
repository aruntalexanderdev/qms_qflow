using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;

namespace Qms.Tests.Support;

/// <summary>One HTTP reply with the parts the auth tests look at.</summary>
public sealed class ApiReply(HttpResponseMessage response, string body)
{
    public int Status => (int)response.StatusCode;

    public string Body => body;

    public HttpResponseMessage Response => response;

    public JsonElement Json => string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;

    public string? Code => Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("code", out var code) ? code.GetString() : null;

    public IReadOnlyList<string> SetCookies =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    /// <summary>The full Set-Cookie header of the refresh cookie, or null.</summary>
    public string? RefreshSetCookie => SetCookies.FirstOrDefault(c => c.StartsWith("qms_refresh=", StringComparison.Ordinal));

    /// <summary>The refresh token value (empty after the cookie was cleared), or null if no refresh cookie was set.</summary>
    public string? RefreshToken => RefreshSetCookie?.Split(';')[0]["qms_refresh=".Length..];
}

/// <summary>
/// The real API host on a copy of the seeded template database, with the configured Super Admin and Company 1 Company
/// Admin (<see cref="TestAccounts"/>). Cookies are handled by hand: the refresh cookie is Secure and the test server is http.
/// </summary>
public sealed class AuthHost : IDisposable
{
    private AuthHost(QmsApiFactory factory, string connectionString)
    {
        Factory = factory;
        ConnectionString = connectionString;
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public QmsApiFactory Factory { get; }

    public string ConnectionString { get; }

    public HttpClient Client { get; }

    /// <summary>Starts the host on an existing database (a clone of a template).</summary>
    public static AuthHost StartOn(string connectionString, string environment = "Development") =>
        new(new QmsApiFactory(environment, connectionString: connectionString), connectionString);

    public static async Task<AuthHost> StartAsync(
        PostgresFixture postgres, string environment = "Development", IReadOnlyDictionary<string, string?>? extraConfiguration = null)
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        var factory = new QmsApiFactory(environment, connectionString: connectionString, extraConfiguration: extraConfiguration);
        return new AuthHost(factory, connectionString);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    public async Task<ApiReply> SendAsync(
        HttpMethod method, string url, object? body = null, string? bearer = null, string? refreshCookie = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"qms_refresh={refreshCookie}");
        }

        var response = await Client.SendAsync(request, CancellationToken.None);
        return new ApiReply(response, await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    public Task<ApiReply> LoginAsync(string userName, string password) =>
        SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { userName, password });

    public Task<ApiReply> LoginCompanyAdminAsync() => LoginAsync(TestAccounts.CompanyAdminUserName, TestAccounts.CompanyAdminPassword);

    public Task<ApiReply> LoginSuperAdminAsync() => LoginAsync(TestAccounts.SuperAdminUserName, TestAccounts.SuperAdminPassword);

    public static string AccessToken(ApiReply loginReply)
    {
        loginReply.Status.ShouldBe(200);
        return loginReply.Json.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Mints an access token for a user that need not exist in the database (only the API's JWT validation is exercised).</summary>
    public string MintToken(CurrentUserDto user, TimeProvider? clock = null)
    {
        using var scope = Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Qms.Infrastructure.Identity.JwtOptions>>();
        return new Qms.Infrastructure.Identity.JwtTokenService(options, clock ?? TimeProvider.System).CreateAccessToken(user).Value;
    }

    public static CurrentUserDto UserOf(Guid? companyId, string role = Roles.CompanyAdmin, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "minted", "Minted User", role, companyId, "en", [], []);

    /// <summary>Adds Company 2 with Branch "X1" and a Company 2 user (setup writes run in a cross-tenant section).</summary>
    public async Task<Company2> AddCompany2Async()
    {
        await using var db = PostgresFixture.CreateContext(ConnectionString);
        var company = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, NameEn = "Other branch", NameAr = "فرع آخر", Code = "X1", Timezone = "Asia/Riyadh" };
        db.Branches.Add(branch);
        var user = new AppUser { UserName = "company2.user", NormalizedUserName = "COMPANY2.USER", DisplayName = "Company 2 User", CompanyId = company.Id };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Company2(company.Id, branch.Id, user.Id);
    }

    public sealed record Company2(Guid CompanyId, Guid BranchId, Guid UserId);

    public QmsDbContext OpenContext(Guid? companyId = null, bool? crossTenant = null) =>
        PostgresFixture.CreateContext(ConnectionString, companyId, crossTenant: crossTenant);

    public static Guid Company1 => SeedIds.Company1;

    public async Task<Guid> SeededUserIdAsync(string userName)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.FindByNameAsync(userName))!.Id;
    }
}
