using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-051 AC-12 to AC-14 (GET /languages) and AC-26 (Accept-Language, one language query per request).
[Collection(PostgresCollection.Name)]
public sealed class LanguagesApiTests(PostgresFixture postgres)
{
    private const string Url = "/api/v1/languages";
    private const string RequestLanguageUrl = "/api/v1/test/setup/request-language";

    private static string[] Codes(ApiReply reply) =>
        [.. reply.Json.GetProperty("languages").EnumerateArray().Select(language => language.GetProperty("code").GetString()!)];

    // ---- AC-12

    [Fact]
    public async Task Ac12_an_anonymous_caller_gets_only_the_available_languages_and_no_company_data()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(null, Url);

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
        var language = reply.Json.GetProperty("languages").EnumerateArray().ShouldHaveSingleItem();
        language.GetProperty("code").GetString().ShouldBe("en");
        language.GetProperty("nativeName").GetString().ShouldBe("English");
        language.GetProperty("direction").GetString().ShouldBe("ltr");
        reply.Json.EnumerateObject().Select(p => p.Name).ShouldBe(["defaultLanguage", "languages"], ignoreOrder: true);
        language.EnumerateObject().Select(p => p.Name).ShouldBe(["code", "nativeName", "direction"], ignoreOrder: true);
        reply.Body.ShouldNotContain("Company");
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    // ---- AC-13

    [Fact]
    public async Task Ac13_a_company_user_gets_the_languages_the_company_enabled_and_anonymous_gets_all_available()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var companyOne = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);
        var companyTwo = await setup.GetAsync(SetupUser.CompanyAdminC2, Url);
        var anonymous = await setup.GetAsync(null, Url);

        Codes(companyOne).ShouldBe(["en", "ar"]);
        Codes(companyTwo).ShouldBe(["en"]);
        Codes(anonymous).ShouldBe(["en", "ar"]);
        foreach (var reply in new[] { companyOne, companyTwo, anonymous })
        {
            reply.Status.ShouldBe(200);
            reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
        }

        var arabic = companyOne.Json.GetProperty("languages")[1];
        arabic.GetProperty("nativeName").GetString().ShouldBe("العربية");
        arabic.GetProperty("direction").GetString().ShouldBe("rtl");
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac13_in_the_phase_1_database_a_company_user_gets_only_english()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        Codes(reply).ShouldBe(["en"]);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
    }

    [Fact]
    public async Task Ac13_every_company_role_gets_the_company_list_without_any_permission()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        foreach (var user in new[] { SetupUser.BranchManagerB1, SetupUser.CounterStaff })
        {
            Codes(await setup.GetAsync(user, Url)).ShouldBe(["en", "ar"], user.ToString());
        }
    }

    [Fact]
    public async Task Ac13_a_language_that_is_enabled_but_no_longer_available_still_reaches_the_company_user_and_not_the_anonymous_caller()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        await setup.MakeArabicUnavailableAsync();

        Codes(await setup.GetAsync(null, Url)).ShouldBe(["en"]);
        Codes(await setup.GetAsync(SetupUser.SuperAdmin, Url)).ShouldBe(["en"]);
        Codes(await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).ShouldBe(["en", "ar"]);
    }

    // ---- AC-14

    [Fact]
    public async Task Ac14_a_super_admin_gets_the_available_registry_languages_not_a_company_setting()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.SuperAdmin, Url);

        reply.Status.ShouldBe(200);
        Codes(reply).ShouldBe(["en", "ar"]);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
    }

    [Fact]
    public async Task Ac14_in_the_phase_1_database_a_super_admin_gets_only_english()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        Codes(await setup.GetAsync(SetupUser.SuperAdmin, Url)).ShouldBe(["en"]);
    }

    [Fact]
    public async Task An_invalid_token_on_the_anonymous_endpoint_is_treated_as_anonymous()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.Host.SendAsync(HttpMethod.Get, Url, bearer: "not-a-token");

        reply.Status.ShouldBe(200);
        Codes(reply).ShouldBe(["en"]);
    }

    // ---- AC-26

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-AE", "ar")]
    [InlineData("fr", "en")]
    [InlineData("fr, ar;q=0.8", "ar")]
    [InlineData(null, "en")]
    public async Task Ac26_a_company_with_arabic_gets_the_negotiated_language(string? header, string expected)
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var reply = await setup.GetWithLanguageAsync(SetupUser.CompanyAdminC1, RequestLanguageUrl, header);

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("language").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task Ac26_a_company_without_arabic_and_an_anonymous_caller_get_english()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        (await setup.GetWithLanguageAsync(SetupUser.CompanyAdminC2, RequestLanguageUrl, "ar")).Json.GetProperty("language").GetString().ShouldBe("en");
        (await setup.GetWithLanguageAsync(null, RequestLanguageUrl, "ar")).Json.GetProperty("language").GetString().ShouldBe("en");
        (await setup.GetWithLanguageAsync(SetupUser.SuperAdmin, RequestLanguageUrl, "ar")).Json.GetProperty("language").GetString().ShouldBe("en");
    }

    [Fact]
    public async Task Ac26_the_language_tables_are_read_once_however_many_validators_and_handlers_use_them()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var service = await setup.GetAsync(SetupUser.CompanyAdminC1, $"/api/v1/service-types/{SeedIds.Service(3)}");

        int LanguageQueries() => setup.Sql.Commands.Count(sql => sql.Contains("FROM language", StringComparison.Ordinal));

        setup.Sql.Clear();
        var create = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { name = "Cards", translations = new { ar = "أ" }, prefix = "H" });
        var createQueries = LanguageQueries();

        setup.Sql.Clear();
        var list = await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types");
        var listQueries = LanguageQueries();

        setup.Sql.Clear();
        var update = await setup.PutAsync(SetupUser.CompanyAdminC1, $"/api/v1/service-types/{SeedIds.Service(3)}", new
        {
            name = "Loans", translations = new { ar = "ب" }, prefix = "C", sortOrder = 3, isActive = true, version = service.Json.GetProperty("version").GetString(),
        });
        var updateQueries = LanguageQueries();

        setup.Sql.Clear();
        var languages = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);
        var languagesQueries = LanguageQueries();

        new[] { create.Status, list.Status, update.Status, languages.Status }.ShouldBe([201, 200, 200, 200]);
        (createQueries, listQueries, updateQueries, languagesQueries).ShouldBe((1, 1, 1, 1));
    }

    [Fact]
    public async Task Ac26_a_request_with_nothing_to_validate_does_not_touch_the_language_tables()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        setup.Sql.Clear();
        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types");

        reply.Status.ShouldBe(200);
        // Listing reads the company languages once (to hide translations of languages that are not enabled).
        setup.Sql.Commands.Count(sql => sql.Contains("FROM language", StringComparison.Ordinal)).ShouldBe(1);
    }
}
