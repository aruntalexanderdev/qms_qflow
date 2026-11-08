using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-051 AC-15 to AC-22 and AC-40 (GET and PUT /company-languages) through the real host.
[Collection(PostgresCollection.Name)]
public sealed class CompanyLanguagesApiTests(PostgresFixture postgres)
{
    private const string Url = "/api/v1/company-languages";

    private static string[] Enabled(ApiReply reply) =>
        [.. reply.Json.GetProperty("enabledLanguages").EnumerateArray().Select(code => code.GetString()!)];

    private static async Task<string> VersionAsync(SetupHost setup, SetupUser user = SetupUser.CompanyAdminC1) =>
        SetupHost.Str(await setup.GetAsync(user, Url), "version");

    private static async Task<string[]> StoredAsync(SetupHost setup, Guid companyId)
    {
        await using var db = setup.Host.OpenContext();
        return (await db.Companies.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == companyId)).EnabledLanguages;
    }

    private static async Task<string> StoredDefaultAsync(SetupHost setup, Guid companyId)
    {
        await using var db = setup.Host.OpenContext();
        return (await db.Companies.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == companyId)).DefaultLanguage;
    }

    // ---- AC-15

    [Fact]
    public async Task Ac15_get_returns_the_setting_the_available_languages_and_a_version()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
        Enabled(reply).ShouldBe(["en", "ar"]);
        var available = reply.Json.GetProperty("availableLanguages").EnumerateArray().ToList();
        available.Select(l => l.GetProperty("code").GetString()).ShouldBe(["en", "ar"]);
        available[1].GetProperty("nativeName").GetString().ShouldBe("العربية");
        available[1].GetProperty("direction").GetString().ShouldBe("rtl");
        SetupHost.Str(reply, "version").ShouldNotBeNullOrEmpty();
        reply.Json.EnumerateObject().Select(p => p.Name).ShouldBe(["defaultLanguage", "enabledLanguages", "availableLanguages", "version"], ignoreOrder: true);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac15_in_the_phase_1_database_only_english_is_enabled_and_available()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        Enabled(reply).ShouldBe(["en"]);
        reply.Json.GetProperty("availableLanguages").EnumerateArray().Select(l => l.GetProperty("code").GetString()).ShouldBe(["en"]);
    }

    // ---- AC-16

    [Fact]
    public async Task Ac16_anonymous_gets_401_and_only_the_company_admin_gets_in()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var body = new { enabledLanguages = new[] { "en", "ar" }, version = await VersionAsync(setup) };

        var anonymousGet = await setup.GetAsync(null, Url);
        var anonymousPut = await setup.PutAsync(null, Url, body);

        anonymousGet.Status.ShouldBe(401);
        anonymousGet.Code.ShouldBe("auth.unauthorized");
        anonymousPut.Status.ShouldBe(401);
        anonymousPut.Code.ShouldBe("auth.unauthorized");
        foreach (var user in new[] { SetupUser.CounterStaff, SetupUser.BranchManagerB1, SetupUser.BranchManagerB2, SetupUser.SuperAdmin })
        {
            var get = await setup.GetAsync(user, Url);
            var put = await setup.PutAsync(user, Url, body);

            get.Status.ShouldBe(403, user.ToString());
            get.Code.ShouldBe("auth.forbidden");
            put.Status.ShouldBe(403, user.ToString());
            put.Code.ShouldBe("auth.forbidden");
        }

        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).Status.ShouldBe(200);
        (await setup.PutAsync(SetupUser.CompanyAdminC1, Url, body)).Status.ShouldBe(200);
        (await setup.AuditCountAsync()).ShouldBe(1);
    }

    // ---- AC-17

    [Fact]
    public async Task Ac17_enabling_arabic_saves_audits_and_tells_every_branch_after_the_commit()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        await setup.MakeArabicAvailableAsync();
        var version = await VersionAsync(setup);
        setup.Notifier.Clear();

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version });

        reply.Status.ShouldBe(200, reply.Body);
        Enabled(reply).ShouldBe(["en", "ar"]);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
        SetupHost.Str(reply, "version").ShouldNotBe(version);
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await VersionAsync(setup)).ShouldBe(SetupHost.Str(reply, "version"));

        var languages = await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/languages");
        languages.Json.GetProperty("languages").EnumerateArray().Select(l => l.GetProperty("code").GetString()).ShouldBe(["en", "ar"]);

        var audit = (await setup.AuditRowsAsync()).ShouldHaveSingleItem();
        (audit.EntityName, audit.Action, audit.CompanyId, audit.UserId).ShouldBe(("company_languages", "update", SeedIds.Company1, setup.Data.UserId(SetupUser.CompanyAdminC1)));
        audit.EntityId.ShouldBe(SeedIds.Company1);
        using var data = JsonDocument.Parse(audit.Data!);
        data.RootElement.GetProperty("enabledLanguages").EnumerateArray().Select(c => c.GetString()).ShouldBe(["en", "ar"]);
        data.RootElement.GetProperty("version").GetString().ShouldBe(version);
        data.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["enabledLanguages", "version"], ignoreOrder: true);

        await using var db = setup.Host.OpenContext();
        var branches = await db.Branches.IgnoreQueryFilters().Where(b => b.CompanyId == SeedIds.Company1).Select(b => b.Id).ToListAsync();
        branches.Count.ShouldBeGreaterThanOrEqualTo(2);
        setup.Notifier.Calls.Select(c => (c.CompanyId, c.BranchId)).ShouldBe(branches.Select(b => (SeedIds.Company1, (Guid?)b)), ignoreOrder: true);
    }

    [Fact]
    public async Task Ac17_the_codes_are_stored_in_registry_form_and_registry_order()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        await setup.MakeArabicAvailableAsync();

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { " AR ", "EN" }, version = await VersionAsync(setup) });

        reply.Status.ShouldBe(200, reply.Body);
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
    }

    // ---- AC-18

    [Fact]
    public async Task Ac18_a_language_that_is_not_available_or_unknown_is_400_and_names_the_code()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var version = await VersionAsync(setup);

        var notAvailable = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version });
        var unknown = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "fr" }, version });

        notAvailable.Status.ShouldBe(400);
        notAvailable.Code.ShouldBe("company_language.not_available");
        notAvailable.Json.GetProperty("errors").GetProperty("enabledLanguages")[0].GetString()!.ShouldContain("'ar'");
        unknown.Status.ShouldBe(400);
        unknown.Code.ShouldBe("company_language.not_available");
        unknown.Json.GetProperty("errors").GetProperty("enabledLanguages")[0].GetString()!.ShouldContain("'fr'");
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en"]);
        (await VersionAsync(setup)).ShouldBe(version);
        (await setup.AuditCountAsync()).ShouldBe(0);
        setup.Notifier.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ac18_a_code_in_another_letter_case_is_accepted_as_the_registry_code()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "EN" }, version = await VersionAsync(setup) });

        reply.Status.ShouldBe(200, reply.Body);
        Enabled(reply).ShouldBe(["en"]);
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en"]);
    }

    // ---- AC-19

    [Fact]
    public async Task Ac19_removing_the_default_is_400_default_required()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var version = await VersionAsync(setup);

        var onlyArabic = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "ar" }, version });
        var empty = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = Array.Empty<string>(), version });

        foreach (var reply in new[] { onlyArabic, empty })
        {
            reply.Status.ShouldBe(400, reply.Body);
            reply.Code.ShouldBe("company_language.default_required");
        }

        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac19_duplicates_and_missing_fields_are_400_validation_failed_naming_the_field()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var version = await VersionAsync(setup);
        var cases = new (string Field, object Body)[]
        {
            ("enabledLanguages", new { enabledLanguages = new[] { "en", "en" }, version }),
            ("enabledLanguages", new { enabledLanguages = new[] { "en", "EN" }, version }),
            ("enabledLanguages", new { enabledLanguages = new[] { "en", "" }, version }),
            ("enabledLanguages", new { version }),
            ("version", new { enabledLanguages = new[] { "en", "ar" } }),
            ("version", new { enabledLanguages = new[] { "en", "ar" }, version = "garbage" }),
        };

        foreach (var (field, body) in cases)
        {
            var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, body);

            reply.Status.ShouldBe(400, JsonSerializer.Serialize(body));
            reply.Code.ShouldBe("validation.failed");
            reply.Json.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue($"{field} in {reply.Body}");
        }

        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac19_a_default_language_in_the_body_is_ignored_and_no_api_changes_it()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var version = await VersionAsync(setup);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, defaultLanguage = "ar", version });
        var withoutEnglish = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "ar" }, defaultLanguage = "ar", version });

        reply.Status.ShouldBe(200, reply.Body);
        reply.Json.GetProperty("defaultLanguage").GetString().ShouldBe("en");
        withoutEnglish.Status.ShouldBe(400);
        withoutEnglish.Code.ShouldBe("company_language.default_required");
        (await StoredDefaultAsync(setup, SeedIds.Company1)).ShouldBe("en");
    }

    // ---- AC-20

    [Fact]
    public async Task Ac20_disabling_a_language_a_dispenser_still_lists_is_409_and_names_the_devices()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        await setup.SetDispenserLanguagesAsync(SeedIds.Dispenser(1), "en", "ar");
        var version = await VersionAsync(setup);
        string dispenser1Name;
        await using (var db = setup.Host.OpenContext())
        {
            dispenser1Name = (await db.Devices.IgnoreQueryFilters().SingleAsync(d => d.Id == SeedIds.Dispenser(1))).Name;
        }

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en" }, version });

        reply.Status.ShouldBe(409, reply.Body);
        reply.Code.ShouldBe("company_language.in_use");
        var devices = reply.Json.GetProperty("devices").EnumerateArray().ToList();
        var device = devices.ShouldHaveSingleItem();
        device.GetProperty("id").GetGuid().ShouldBe(SeedIds.Dispenser(1));
        device.GetProperty("name").GetString().ShouldBe(dispenser1Name);
        reply.Body.ShouldNotContain(SeedIds.Dispenser(2).ToString());
        reply.Json.GetProperty("languages").EnumerateArray().Select(l => l.GetString()).ShouldBe(["ar"]);
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await VersionAsync(setup)).ShouldBe(version);
        (await setup.AuditCountAsync()).ShouldBe(0);
        setup.Notifier.Calls.ShouldBeEmpty();

        await setup.SetDispenserLanguagesAsync(SeedIds.Dispenser(1), "en");
        var retry = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en" }, version });

        retry.Status.ShouldBe(200, retry.Body);
        Enabled(retry).ShouldBe(["en"]);
        await using var after = setup.Host.OpenContext();
        (await after.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == SeedIds.Service(1))).NameTranslations.ShouldContainKey("ar");
    }

    [Fact]
    public async Task Ac20_a_dispenser_of_another_company_does_not_block()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        // Company 2 never enabled ar; its dispenser (no settings row) and Branch 2's dispenser must not matter either.
        var version = await VersionAsync(setup);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en" }, version });

        reply.Status.ShouldBe(200, reply.Body);
    }

    // ---- AC-21

    [Fact]
    public async Task Ac21_a_stale_version_is_409_and_an_equal_put_is_audited_without_an_update()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        await setup.MakeArabicAvailableAsync();
        var v1 = await VersionAsync(setup);
        var first = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version = v1 });
        first.Status.ShouldBe(200, first.Body);
        var v2 = SetupHost.Str(first, "version");
        v2.ShouldNotBe(v1);

        var stale = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version = v1 });
        var staleChange = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en" }, version = v1 });

        stale.Status.ShouldBe(409);
        stale.Code.ShouldBe("request.conflict");
        staleChange.Status.ShouldBe(409);
        staleChange.Code.ShouldBe("request.conflict");
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await setup.AuditCountAsync()).ShouldBe(1);

        setup.Sql.Clear();
        setup.Notifier.Clear();
        var same = await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "ar", "en" }, version = v2 });

        same.Status.ShouldBe(200, same.Body);
        SetupHost.Str(same, "version").ShouldBe(v2);
        (await VersionAsync(setup)).ShouldBe(v2);
        (await setup.AuditCountAsync()).ShouldBe(2);
        setup.Sql.Commands.ShouldNotContain(sql => sql.Contains("UPDATE company", StringComparison.Ordinal));
        setup.Notifier.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ac21_a_real_change_does_send_an_update_company()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        await setup.MakeArabicAvailableAsync();

        setup.Sql.Clear();
        (await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version = await VersionAsync(setup) })).Status.ShouldBe(200);

        setup.Sql.Commands.ShouldContain(sql => sql.Contains("UPDATE company", StringComparison.Ordinal));
    }

    // ---- AC-22

    [Fact]
    public async Task Ac22_company_2_sees_and_changes_only_its_own_setting()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var company1Version = await VersionAsync(setup);
        var company2Get = await setup.GetAsync(SetupUser.CompanyAdminC2, Url);

        Enabled(company2Get).ShouldBe(["en"]);
        var company2Version = SetupHost.Str(company2Get, "version");
        company2Version.ShouldNotBe(company1Version);

        var foreignVersion = await setup.PutAsync(SetupUser.CompanyAdminC2, Url, new { enabledLanguages = new[] { "en", "ar" }, version = company1Version });

        foreignVersion.Status.ShouldBe(409);
        foreignVersion.Code.ShouldBe("request.conflict");
        (await StoredAsync(setup, SetupTestData.Company2)).ShouldBe(["en"]);
        (await setup.AuditCountAsync()).ShouldBe(0);

        var own = await setup.PutAsync(SetupUser.CompanyAdminC2, Url, new { enabledLanguages = new[] { "en", "ar" }, version = company2Version });

        own.Status.ShouldBe(200, own.Body);
        (await StoredAsync(setup, SetupTestData.Company2)).ShouldBe(["en", "ar"]);
        (await StoredAsync(setup, SeedIds.Company1)).ShouldBe(["en", "ar"]);
        (await VersionAsync(setup)).ShouldBe(company1Version);
        var audit = (await setup.AuditRowsAsync()).ShouldHaveSingleItem();
        audit.CompanyId.ShouldBe(SetupTestData.Company2);
        setup.Notifier.Calls.ShouldAllBe(call => call.CompanyId == SetupTestData.Company2);
        setup.Notifier.Calls.Select(c => c.BranchId).ShouldBe([SetupTestData.Company2Branch]);
    }

    // ---- AC-40

    [Fact]
    public async Task Ac40_only_a_200_outcome_writes_an_audit_row_and_reads_write_none()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var version = await VersionAsync(setup);
        await setup.SetDispenserLanguagesAsync(SeedIds.Dispenser(1), "en", "ar");

        await setup.GetAsync(SetupUser.CompanyAdminC1, Url);
        await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/languages");
        await setup.GetAsync(null, "/api/v1/languages");
        await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "ar" }, version });
        await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "xx" }, version });
        await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en" }, version });
        await setup.PutAsync(SetupUser.CounterStaff, Url, new { enabledLanguages = new[] { "en" }, version });
        await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version = "AAAAAA" });
        await setup.PutAsync(null, Url, new { enabledLanguages = new[] { "en" }, version });
        (await setup.AuditCountAsync()).ShouldBe(0);

        (await setup.PutAsync(SetupUser.CompanyAdminC1, Url, new { enabledLanguages = new[] { "en", "ar" }, version })).Status.ShouldBe(200);

        var audit = (await setup.AuditRowsAsync()).ShouldHaveSingleItem();
        audit.Data!.ShouldNotContain("@");
    }
}
