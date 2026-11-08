using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Tests.Support;

namespace Qms.Tests.Application;

// STORY-051 AC-23 to AC-26 (unit): the pure localisation rules and the Accept-Language negotiation.
public sealed class LocalizedTextRulesTests
{
    private static readonly CompanyLanguageSettings Both = new("en", ["en", "ar"]);
    private static readonly CompanyLanguageSettings EnglishOnly = CompanyLanguageSettings.English;

    private static Dictionary<string, string?> T(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    // ---- AC-23

    [Fact]
    public void Ac23_a_valid_key_passes_in_any_letter_case_and_is_stored_in_registry_form()
    {
        foreach (var key in new[] { "ar", "AR", "Ar", " ar " })
        {
            var check = LocalizedTextRules.Check(T((key, "البطاقات")), Both, 200);

            check.IsValid.ShouldBeTrue(key);
            check.Normalized.ShouldBe(new Dictionary<string, string> { ["ar"] = "البطاقات" });
        }
    }

    [Fact]
    public void Ac23_a_blank_or_null_value_is_dropped_without_an_error_even_for_a_language_that_is_not_enabled()
    {
        foreach (var settings in new[] { Both, EnglishOnly })
        {
            foreach (var value in new string?[] { "  ", "", null })
            {
                var check = LocalizedTextRules.Check(T(("ar", value), ("fr", value), ("en", value)), settings, 200);

                check.IsValid.ShouldBeTrue();
                check.Normalized.ShouldBeEmpty();
            }
        }
    }

    [Fact]
    public void Ac23_a_value_is_trimmed_and_limited_to_the_field_length()
    {
        LocalizedTextRules.Check(T(("ar", "  x  ")), Both, 200).Normalized["ar"].ShouldBe("x");
        LocalizedTextRules.Check(T(("ar", new string('ع', 200))), Both, 200).IsValid.ShouldBeTrue();

        var tooLong = LocalizedTextRules.Check(T(("ar", new string('ع', 201))), Both, 200);
        tooLong.Failures.Select(f => f.Key).ShouldBe(["ar"]);
        tooLong.Failures.Single().ErrorCode.ShouldBeNull();

        // A limit of 500 (header, footer, consent text) is accepted for other fields.
        LocalizedTextRules.Check(T(("ar", new string('ع', 500))), Both, 500).IsValid.ShouldBeTrue();
        LocalizedTextRules.Check(T(("ar", new string('ع', 501))), Both, 500).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Ac23_the_default_language_as_a_key_and_a_language_that_is_not_enabled_have_their_own_codes()
    {
        var english = LocalizedTextRules.Check(T(("en", "Cards")), Both, 200);
        var french = LocalizedTextRules.Check(T(("fr", "Cartes")), Both, 200);
        var upperEnglish = LocalizedTextRules.Check(T(("EN", "Cards")), Both, 200);

        english.Failures.ShouldHaveSingleItem().ShouldBe(new TranslationFailure("en", english.Failures[0].Message, "translation.default_language_key"));
        french.Failures.ShouldHaveSingleItem().ShouldBe(new TranslationFailure("fr", french.Failures[0].Message, "translation.language_not_enabled"));
        upperEnglish.Failures.Single().Key.ShouldBe("en");
        upperEnglish.Failures.Single().ErrorCode.ShouldBe("translation.default_language_key");
    }

    [Fact]
    public void Ac23_with_only_english_enabled_arabic_is_not_enabled()
    {
        var check = LocalizedTextRules.Check(T(("ar", "البطاقات")), EnglishOnly, 200);

        check.Failures.ShouldHaveSingleItem();
        check.Failures[0].Key.ShouldBe("ar");
        check.Failures[0].ErrorCode.ShouldBe("translation.language_not_enabled");
        check.Normalized.ShouldBeEmpty();
    }

    [Fact]
    public void Ac23_keys_that_differ_only_by_case_are_refused()
    {
        var check = LocalizedTextRules.Check(T(("ar", "x"), ("AR", "y")), Both, 200);

        check.IsValid.ShouldBeFalse();
        check.Failures.ShouldContain(failure => failure.Key == "ar");
    }

    [Fact]
    public void Ac23_the_default_value_is_one_to_the_limit_characters_after_trimming()
    {
        string[] Failures(string? value, int max = 200)
        {
            var validator = new FluentValidation.InlineValidator<Holder>();
            validator.RuleFor(holder => holder.Value).DefaultValue(max);
            return [.. validator.Validate(new Holder(value)).Errors.Select(error => error.PropertyName)];
        }

        Failures("Cards").ShouldBeEmpty();
        Failures("  Cards  ").ShouldBeEmpty();
        Failures(new string('x', 200)).ShouldBeEmpty();
        Failures(new string('x', 201)).ShouldBe(["Value"]);
        Failures(new string('x', 500), 500).ShouldBeEmpty();
        Failures(new string('x', 501), 500).ShouldBe(["Value"]);
        Failures("   ").ShouldBe(["Value"]);
        Failures("").ShouldBe(["Value"]);
        Failures(null).ShouldBe(["Value"]);
    }

    private sealed record Holder(string? Value);

    // ---- AC-24

    private static readonly Dictionary<string, string> Stored = new() { ["ar"] = "البطاقات", ["fr"] = "Cartes" };

    [Fact]
    public void Ac24_merge_replaces_enabled_keys_removes_blank_ones_and_always_keeps_a_disabled_language()
    {
        LocalizedTextRules.Merge(Stored, T(("ar", "بطاقات")), Both)
            .ShouldBe(new Dictionary<string, string> { ["ar"] = "بطاقات", ["fr"] = "Cartes" }, ignoreOrder: true);
        LocalizedTextRules.Merge(Stored, T(), Both)
            .ShouldBe(new Dictionary<string, string> { ["fr"] = "Cartes" });
        LocalizedTextRules.Merge(Stored, T(("ar", "")), Both)
            .ShouldBe(new Dictionary<string, string> { ["fr"] = "Cartes" });
        LocalizedTextRules.Merge(Stored, null, Both)
            .ShouldBe(new Dictionary<string, string> { ["fr"] = "Cartes" });
    }

    [Fact]
    public void Ac24_merge_with_only_english_enabled_keeps_the_stored_arabic()
    {
        LocalizedTextRules.Merge(Stored, T(), EnglishOnly).ShouldBe(Stored);
        LocalizedTextRules.Merge(Stored, T(("ar", "ignored")), EnglishOnly).ShouldBe(Stored);
        LocalizedTextRules.Merge(null, T(("AR", " بطاقات ")), Both).ShouldBe(new Dictionary<string, string> { ["ar"] = "بطاقات" });
    }

    [Fact]
    public void Ac24_for_admin_returns_only_enabled_non_default_languages()
    {
        var merged = LocalizedTextRules.Merge(Stored, T(("ar", "بطاقات")), Both);

        LocalizedTextRules.ForAdmin(merged, Both).ShouldBe(new Dictionary<string, string> { ["ar"] = "بطاقات" });
        LocalizedTextRules.ForAdmin(merged, EnglishOnly).ShouldBeEmpty();
        LocalizedTextRules.ForAdmin(new Dictionary<string, string> { ["en"] = "Cards", ["ar"] = " " }, Both).ShouldBeEmpty();
        LocalizedTextRules.ForAdmin(null, Both).ShouldBeEmpty();
    }

    // ---- AC-25

    private static readonly Dictionary<string, string> Arabic = new() { ["ar"] = "البطاقات" };

    [Fact]
    public void Ac25_resolve_uses_the_translation_of_an_enabled_language_and_falls_back_to_the_default_value()
    {
        LocalizedTextRules.Resolve("Cards", Arabic, "ar", Both).ShouldBe("البطاقات");
        LocalizedTextRules.Resolve("Cards", Arabic, "en", Both).ShouldBe("Cards");
        LocalizedTextRules.Resolve("Cards", Arabic, "fr", Both).ShouldBe("Cards");
        LocalizedTextRules.Resolve("Cards", Arabic, "AR", Both).ShouldBe("البطاقات");
        LocalizedTextRules.Resolve("Loans", new Dictionary<string, string>(), "ar", Both).ShouldBe("Loans");
        LocalizedTextRules.Resolve("Cards", Arabic, null, Both).ShouldBe("Cards");
        LocalizedTextRules.Resolve("Cards", null, "ar", Both).ShouldBe("Cards");
    }

    [Fact]
    public void Ac25_a_language_that_is_not_enabled_is_treated_as_the_default()
    {
        LocalizedTextRules.Resolve("Cards", Arabic, "ar", EnglishOnly).ShouldBe("Cards");
    }

    [Fact]
    public void Ac25_resolve_all_returns_every_requested_key_each_with_its_own_fallback()
    {
        string[] device = ["en", "ar"];

        LocalizedTextRules.ResolveAll("Cards", Arabic, device, Both)
            .ShouldBe(new Dictionary<string, string> { ["en"] = "Cards", ["ar"] = "البطاقات" });
        LocalizedTextRules.ResolveAll("Loans", new Dictionary<string, string>(), device, Both)
            .ShouldBe(new Dictionary<string, string> { ["en"] = "Loans", ["ar"] = "Loans" });
        LocalizedTextRules.ResolveAll("Cards", Arabic, device, EnglishOnly)
            .ShouldBe(new Dictionary<string, string> { ["en"] = "Cards", ["ar"] = "Cards" });
        LocalizedTextRules.ResolveAll("Cards", Arabic, [], Both).ShouldBeEmpty();
    }

    // ---- AC-26 (negotiation)

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-AE", "ar")]
    [InlineData("AR", "ar")]
    [InlineData("fr", "en")]
    [InlineData("fr, ar;q=0.8", "ar")]
    [InlineData("ar;q=0.5, en;q=0.9", "en")]
    [InlineData("ar;q=0, en", "en")]
    [InlineData("*", "en")]
    [InlineData("fr-CA, fr;q=0.9, ar-EG;q=0.5", "ar")]
    [InlineData("ar;q=abc", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Ac26_the_best_offered_language_wins_by_quality_and_primary_subtag(string? header, string expected) =>
        AcceptLanguageNegotiator.Negotiate(header, ["en", "ar"], "en").ShouldBe(expected);

    [Fact]
    public void Ac26_only_offered_languages_are_chosen_and_the_fallback_is_the_default()
    {
        AcceptLanguageNegotiator.Negotiate("ar", ["en"], "en").ShouldBe("en");
        AcceptLanguageNegotiator.Negotiate("ar", [], "en").ShouldBe("en");
    }

    // ---- registry order helper and fake port

    [Fact]
    public async Task The_fake_port_counts_reads_so_a_test_can_see_memoisation_is_not_its_job()
    {
        var fake = new FakeCompanyLanguages();
        (await fake.GetCompanySettingsAsync(default)).ShouldBe(CompanyLanguageSettings.English);
        fake.Reads.ShouldBe(1);
    }

    [Fact]
    public void Codes_are_put_in_registry_order()
    {
        new[] { "ar", "en" }.InRegistryOrder(FakeCompanyLanguages.Registry).ShouldBe(["en", "ar"]);
        new[] { "xx", "ar", "en" }.InRegistryOrder(FakeCompanyLanguages.Registry).ShouldBe(["en", "ar", "xx"]);
    }
}
