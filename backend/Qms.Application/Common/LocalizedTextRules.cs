using FluentValidation;
using FluentValidation.Results;
using Qms.Application.Localisation;

namespace Qms.Application.Common;

/// <summary>One problem found in the translations of a field: <c>Key</c> is the language code the error belongs to.</summary>
public sealed record TranslationFailure(string Key, string Message, string? ErrorCode);

/// <summary>Outcome of <see cref="LocalizedTextRules.Check"/>: the failures and the cleaned translations of the enabled languages.</summary>
public sealed record TranslationCheck(IReadOnlyList<TranslationFailure> Failures, IReadOnlyDictionary<string, string> Normalized)
{
    public bool IsValid => Failures.Count == 0;
}

/// <summary>
/// The one place that validates, stores and resolves translated master data (ADR-010 sections 5 to 7): a required
/// default-language value plus optional translations (language code to text, never the default language).
/// The functions are pure (no database); validators get the company's languages from <see cref="ICompanyLanguages"/>.
/// </summary>
public static class LocalizedTextRules
{
    /// <summary>Length limit of names (service type, priority type, zone, branch, company).</summary>
    public const int NameMaxLength = 200;

    public const string LanguageNotEnabledCode = ErrorCodes.TranslationLanguageNotEnabled;
    public const string DefaultLanguageKeyCode = ErrorCodes.TranslationDefaultLanguageKey;

    // ---- validation

    /// <summary>Default-language value: required, 1 to <paramref name="maxLength"/> characters after trimming.</summary>
    public static IRuleBuilderOptions<T, string?> DefaultValue<T>(this IRuleBuilder<T, string?> rule, int maxLength = NameMaxLength) =>
        rule.Must(value => value is not null && value.Trim().Length >= 1 && value.Trim().Length <= maxLength)
            .WithMessage($"'{{PropertyName}}' is required and must be 1 to {maxLength} characters.");

    /// <summary>
    /// Translations: optional. Each problem is its own error keyed <c>&lt;property&gt;.&lt;code&gt;</c> (see <see cref="Check"/>).
    /// The company languages are read only when there is something to check.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, IReadOnlyDictionary<string, string?>?> Translations<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string?>?> rule, ICompanyLanguages languages, int maxLength = NameMaxLength) =>
        rule.CustomAsync(async (translations, context, cancellationToken) =>
        {
            if (translations is not { Count: > 0 })
            {
                return;
            }

            var settings = await languages.GetCompanySettingsAsync(cancellationToken);
            foreach (var failure in Check(translations, settings, maxLength).Failures)
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath + "." + failure.Key, failure.Message) { ErrorCode = failure.ErrorCode! });
            }
        });

    /// <summary>
    /// Checks submitted translations against the company languages (BR-6 to BR-8):
    /// keys that differ only by letter case are refused; a blank value means "no translation" and is dropped without error;
    /// otherwise the default language as a key is <see cref="DefaultLanguageKeyCode"/>, a language that is not enabled is
    /// <see cref="LanguageNotEnabledCode"/>, and a value longer than <paramref name="maxLength"/> (after trimming) fails.
    /// The cleaned result holds registry-form keys and trimmed values.
    /// </summary>
    public static TranslationCheck Check(IReadOnlyDictionary<string, string?>? submitted, CompanyLanguageSettings languages, int maxLength)
    {
        var failures = new List<TranslationFailure>();
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (submitted is null)
        {
            return new TranslationCheck(failures, normalized);
        }

        foreach (var duplicate in submitted.Keys.GroupBy(key => KeyOf(key)).Where(group => group.Count() > 1))
        {
            failures.Add(new TranslationFailure(duplicate.Key, "The language is given more than once.", null));
        }

        foreach (var (key, text) in submitted)
        {
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var code = KeyOf(key);
            if (languages.IsDefault(key))
            {
                failures.Add(new TranslationFailure(code, "The default language is not a translation: send it as the main value.", DefaultLanguageKeyCode));
            }
            else if (languages.Match(key) is not { } registryCode)
            {
                failures.Add(new TranslationFailure(code, "This language is not enabled.", LanguageNotEnabledCode));
            }
            else if (value.Length > maxLength)
            {
                failures.Add(new TranslationFailure(registryCode, $"A translation must be at most {maxLength} characters.", null));
            }
            else
            {
                normalized[registryCode] = value;
            }
        }

        return new TranslationCheck(failures, normalized);
    }

    private static string KeyOf(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

    // ---- storage

    /// <summary>
    /// The translations to store on a <c>PUT</c> (BR-9): the submitted ones for the enabled languages replace the stored
    /// ones, a blank value removes a key, and stored keys of languages that are not enabled are kept unchanged.
    /// Pass an empty <paramref name="stored"/> on create.
    /// </summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string>? stored, IReadOnlyDictionary<string, string?>? submitted, CompanyLanguageSettings languages)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, text) in stored ?? new Dictionary<string, string>())
        {
            if (languages.Match(key) is null && !string.IsNullOrWhiteSpace(text))
            {
                result[key] = text;
            }
        }

        foreach (var (key, text) in submitted ?? new Dictionary<string, string?>())
        {
            if (languages.IsDefault(key) || languages.Match(key) is not { } registryCode || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            result[registryCode] = text.Trim();
        }

        return result;
    }

    /// <summary>The translations an admin API returns (BR-11): only enabled languages, never the default, never blank texts.</summary>
    public static IReadOnlyDictionary<string, string> ForAdmin(IReadOnlyDictionary<string, string>? translations, CompanyLanguageSettings languages)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (translations is null)
        {
            return result;
        }

        foreach (var code in languages.EnabledLanguages.Where(code => !languages.IsDefault(code)))
        {
            if (translations.TryGetValue(code, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result[code] = text.Trim();
            }
        }

        return result;
    }

    /// <summary>True when both objects hold the same keys and texts.</summary>
    public static bool Same(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var other) && other == pair.Value);

    // ---- fallback

    /// <summary>
    /// One text in <paramref name="language"/> (BR-10): the translation when the language is enabled and has a value,
    /// otherwise the default-language value. A language that is not enabled, or is unknown, is treated as the default.
    /// </summary>
    public static string Resolve(string value, IReadOnlyDictionary<string, string>? translations, string? language, CompanyLanguageSettings languages)
    {
        if (translations is not null
            && languages.Match(language) is { } code
            && !languages.IsDefault(code)
            && translations.TryGetValue(code, out var text)
            && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return value;
    }

    /// <summary>The text in every requested language, each resolved with its own fallback; every requested key is present (device APIs).</summary>
    public static Dictionary<string, string> ResolveAll(
        string value, IReadOnlyDictionary<string, string>? translations, IEnumerable<string> requestedLanguages, CompanyLanguageSettings languages)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in requestedLanguages)
        {
            result[language] = Resolve(value, translations, language, languages);
        }

        return result;
    }
}
