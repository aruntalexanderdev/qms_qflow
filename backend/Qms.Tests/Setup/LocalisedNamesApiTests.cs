using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-051 AC-27 to AC-30: service types and priority types on LocalizedTextRules, in the phase 1 database and in the
// ar-enabled test database. Both resources run the same checks.
[Collection(PostgresCollection.Name)]
public sealed class LocalisedNamesApiTests(PostgresFixture postgres)
{
    private const string CompanyLanguagesUrl = "/api/v1/company-languages";

    public static TheoryData<string> Resources => ["service-types", "priority-types"];

    private static string Url(string resource) => "/api/v1/" + resource;

    private static object CreateBody(string resource, string name, string prefix, object? translations = null) =>
        resource == "service-types"
            ? new { name, translations, prefix }
            : new { name, translations, prefix, priorityLevel = 3, isPaid = false };

    private static object UpdateBody(string resource, JsonElement current, string name, object? translations)
    {
        var version = current.GetProperty("version").GetString();
        var prefix = current.GetProperty("prefix").GetString();
        var isActive = current.GetProperty("isActive").GetBoolean();
        return resource == "service-types"
            ? new { name, translations, prefix, sortOrder = current.GetProperty("sortOrder").GetInt32(), isActive, version }
            : new { name, translations, prefix, priorityLevel = current.GetProperty("priorityLevel").GetInt32(), isPaid = false, isActive, version };
    }

    private static async Task<JsonElement> ReadAsync(SetupHost setup, string resource, Guid id) =>
        (await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}")).Json.Clone();

    private static async Task<Dictionary<string, string>> StoredTranslationsAsync(SetupHost setup, string resource, Guid id)
    {
        await using var db = setup.Host.OpenContext();
        return resource == "service-types"
            ? (await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id)).NameTranslations
            : (await db.PriorityTypes.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id)).NameTranslations;
    }

    private static Dictionary<string, string> Translations(JsonElement element) =>
        element.GetProperty("translations").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

    // ---- AC-27

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac27_in_phase_1_only_english_is_enabled_so_arabic_french_and_english_keys_are_400(string resource)
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var before = (await setup.GetAsync(SetupUser.CompanyAdminC1, Url(resource))).Json.GetArrayLength();
        var cases = new (string Key, string Code, object Translations)[]
        {
            ("ar", "translation.language_not_enabled", new { ar = "البطاقات" }),
            ("fr", "translation.language_not_enabled", new { fr = "Cartes" }),
            ("en", "translation.default_language_key", new { en = "Cards" }),
        };

        foreach (var (key, code, translations) in cases)
        {
            var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", "H", translations));

            reply.Status.ShouldBe(400, reply.Body);
            reply.Code.ShouldBe(code);
            reply.Json.GetProperty("errors").TryGetProperty("translations." + key, out _).ShouldBeTrue(reply.Body);
        }

        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url(resource))).Json.GetArrayLength().ShouldBe(before);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac27_empty_or_blank_translations_are_dropped_not_an_error(string resource)
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var bodies = new object?[] { null, new { }, new { ar = "" }, new { ar = "   " }, new { ar = (string?)null }, new { fr = "", en = "" } };
        var prefixes = new[] { "H", "J", "K", "L", "M", "O" };

        for (var i = 0; i < bodies.Length; i++)
        {
            var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", prefixes[i], bodies[i]));

            reply.Status.ShouldBe(201, reply.Body);
            reply.Json.GetProperty("translations").EnumerateObject().ShouldBeEmpty();
            (await StoredTranslationsAsync(setup, resource, SetupHost.Id(reply))).ShouldBeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac27_failures_with_different_codes_give_validation_failed(string resource)
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", "H", new { ar = "x", en = "y" }));

        reply.Status.ShouldBe(400);
        reply.Code.ShouldBe("validation.failed");
        var errors = reply.Json.GetProperty("errors");
        errors.TryGetProperty("translations.ar", out _).ShouldBeTrue(reply.Body);
        errors.TryGetProperty("translations.en", out _).ShouldBeTrue(reply.Body);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac27_a_put_with_arabic_is_400_in_phase_1_too(string resource)
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var id = resource == "service-types" ? SeedIds.Service(3) : SeedIds.Vip;
        var current = await ReadAsync(setup, resource, id);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, current, "Renamed", new { ar = "ع" }));

        reply.Status.ShouldBe(400, reply.Body);
        reply.Code.ShouldBe("translation.language_not_enabled");
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    // ---- AC-28

    [Fact]
    public async Task Ac28_seeded_arabic_service_names_are_stored_but_not_returned_in_phase_1()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var list = await setup.GetAsync(SetupUser.CompanyAdminC1, Url("service-types"));
        var one = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url("service-types")}/{SeedIds.Service(2)}");

        var items = list.Json.EnumerateArray().ToList();
        items.Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["A", "B", "C", "D", "F", "G"]);
        items.Select(i => i.GetProperty("sortOrder").GetInt32()).ShouldBe([1, 2, 3, 4, 5, 6]);
        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["Service 1", "Service 2", "Service 3", "Service 4", "Service 5", "Service 6"]);
        items.ShouldAllBe(i => !i.GetProperty("translations").EnumerateObject().Any());
        one.Json.GetProperty("name").GetString().ShouldBe("Service 2");
        one.Json.GetProperty("translations").EnumerateObject().ShouldBeEmpty();
        list.Body.ShouldNotContain("الخدمة");
        (await StoredTranslationsAsync(setup, "service-types", SeedIds.Service(2))).ShouldContainKey("ar");
    }

    [Fact]
    public async Task Ac28_seeded_arabic_priority_names_are_stored_but_not_returned_in_phase_1()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var list = await setup.GetAsync(SetupUser.CompanyAdminC1, Url("priority-types"));
        var vip = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url("priority-types")}/{SeedIds.Vip}");

        list.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["V", "N"]);
        list.Json.EnumerateArray().ShouldAllBe(i => !i.GetProperty("translations").EnumerateObject().Any());
        vip.Json.GetProperty("translations").EnumerateObject().ShouldBeEmpty();
        (await StoredTranslationsAsync(setup, "priority-types", SeedIds.Vip)).ShouldContainKey("ar");
        (await StoredTranslationsAsync(setup, "priority-types", SeedIds.Normal)).ShouldContainKey("ar");
    }

    [Fact]
    public async Task Ac28_a_put_in_phase_1_keeps_the_stored_arabic()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var current = await ReadAsync(setup, "service-types", SeedIds.Service(3));

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url("service-types")}/{SeedIds.Service(3)}", UpdateBody("service-types", current, "Loans", null));

        reply.Status.ShouldBe(200, reply.Body);
        reply.Json.GetProperty("translations").EnumerateObject().ShouldBeEmpty();
        (await StoredTranslationsAsync(setup, "service-types", SeedIds.Service(3))).ShouldContainKey("ar");
    }

    // ---- AC-29

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac29_arabic_is_saved_trimmed_in_registry_form_and_returned_when_enabled(string resource)
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var create = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", "H", new Dictionary<string, string> { ["AR"] = " البطاقات " }));

        create.Status.ShouldBe(201, create.Body);
        Translations(create.Json).ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });
        var id = SetupHost.Id(create);
        var read = await ReadAsync(setup, resource, id);
        Translations(read).ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });
        (await StoredTranslationsAsync(setup, resource, id)).ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });

        var update = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, read, "Cards", new { ar = "بطاقات" }));
        update.Status.ShouldBe(200, update.Body);
        Translations(update.Json).ShouldBe(new Dictionary<string, string> { ["ar"] = "بطاقات" });

        var french = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, update.Json, "Cards", new { fr = "Cartes" }));
        french.Status.ShouldBe(400);
        french.Code.ShouldBe("translation.language_not_enabled");
        var english = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, update.Json, "Cards", new { en = "Cards" }));
        english.Status.ShouldBe(400);
        english.Code.ShouldBe("translation.default_language_key");

        var cleared = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, update.Json, "Cards", new { }));
        cleared.Status.ShouldBe(200, cleared.Body);
        Translations(cleared.Json).ShouldBeEmpty();
        (await StoredTranslationsAsync(setup, resource, id)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac29_company_2_without_arabic_cannot_send_it_while_company_1_can(string resource)
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var company2 = await setup.PostAsync(SetupUser.CompanyAdminC2, Url(resource), CreateBody(resource, "Cards", "H", new { ar = "x" }));
        var company1 = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", "H", new { ar = "x" }));

        company2.Status.ShouldBe(400);
        company2.Code.ShouldBe("translation.language_not_enabled");
        company1.Status.ShouldBe(201);
    }

    [Fact]
    public async Task Ac29_the_seeded_arabic_is_returned_once_arabic_is_enabled()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);

        var service = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url("service-types")}/{SeedIds.Service(2)}");
        var vip = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url("priority-types")}/{SeedIds.Vip}");

        service.Json.GetProperty("translations").GetProperty("ar").GetString().ShouldBe("الخدمة 2");
        vip.Json.GetProperty("translations").GetProperty("ar").GetString().ShouldNotBeNullOrEmpty();
    }

    // ---- AC-30

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Ac30_a_put_keeps_the_translations_of_a_disabled_language_and_they_return_after_enabling(string resource)
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var created = await setup.PostAsync(SetupUser.CompanyAdminC1, Url(resource), CreateBody(resource, "Cards", "H", new { ar = "البطاقات" }));
        created.Status.ShouldBe(201, created.Body);
        var id = SetupHost.Id(created);
        var languages = await setup.GetAsync(SetupUser.CompanyAdminC1, CompanyLanguagesUrl);

        var disable = await setup.PutAsync(SetupUser.CompanyAdminC1, CompanyLanguagesUrl, new { enabledLanguages = new[] { "en" }, version = SetupHost.Str(languages, "version") });
        disable.Status.ShouldBe(200, disable.Body);

        var hidden = await ReadAsync(setup, resource, id);
        Translations(hidden).ShouldBeEmpty();

        var put = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, hidden, "Cards and loans", null));
        put.Status.ShouldBe(200, put.Body);
        put.Json.GetProperty("name").GetString().ShouldBe("Cards and loans");
        Translations(put.Json).ShouldBeEmpty();
        (await StoredTranslationsAsync(setup, resource, id)).ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });

        // Even a PUT that tries to send Arabic cannot overwrite it while the language is off.
        var refused = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url(resource)}/{id}", UpdateBody(resource, put.Json, "Cards and loans", new { ar = "x" }));
        refused.Status.ShouldBe(400);
        refused.Code.ShouldBe("translation.language_not_enabled");

        var enable = await setup.PutAsync(SetupUser.CompanyAdminC1, CompanyLanguagesUrl, new { enabledLanguages = new[] { "en", "ar" }, version = SetupHost.Str(disable, "version") });
        enable.Status.ShouldBe(200, enable.Body);
        var back = await ReadAsync(setup, resource, id);
        Translations(back).ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });
        back.GetProperty("name").GetString().ShouldBe("Cards and loans");
    }

    [Fact]
    public async Task Ac30_the_seeded_arabic_survives_disabling_and_a_put_of_a_seeded_row()
    {
        using var setup = await SetupHost.StartWithArabicAsync(postgres);
        var languages = await setup.GetAsync(SetupUser.CompanyAdminC1, CompanyLanguagesUrl);
        (await setup.PutAsync(SetupUser.CompanyAdminC1, CompanyLanguagesUrl, new { enabledLanguages = new[] { "en" }, version = SetupHost.Str(languages, "version") })).Status.ShouldBe(200);
        var current = await ReadAsync(setup, "service-types", SeedIds.Service(2));

        var put = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url("service-types")}/{SeedIds.Service(2)}", UpdateBody("service-types", current, "Service two", null));

        put.Status.ShouldBe(200, put.Body);
        (await StoredTranslationsAsync(setup, "service-types", SeedIds.Service(2))).ShouldBe(new Dictionary<string, string> { ["ar"] = "الخدمة 2" });
    }
}
