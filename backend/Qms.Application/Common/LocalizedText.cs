using FluentValidation;
using FluentValidation.Results;

namespace Qms.Application.Common;

/// <summary>
/// Master-data names as the API sees them: a required default-language <c>name</c> (English) and optional
/// <c>translations</c> (language code to text). TEMPORARY STORAGE MAPPING until the localisation migration story
/// (<c>name</c> + <c>name_translations</c> jsonb): <c>name</c> is stored in <c>name_en</c>, <c>translations["ar"]</c> in
/// <c>name_ar</c> (empty text when absent), and any other language is refused. This class is the only place that knows the
/// mapping; the migration story changes only <see cref="ToStorage"/>, <see cref="FromStorage"/> and
/// <see cref="UnsupportedLanguages"/>.
/// </summary>
public static class LocalizedText
{
    public const int MaxLength = 200;
    public const string LanguageNotEnabledCode = ErrorCodes.TranslationLanguageNotEnabled;
    public const string DefaultLanguageKeyCode = ErrorCodes.TranslationDefaultLanguageKey;
    private const string DefaultLanguage = "en";
    private const string Arabic = "ar";

    /// <summary>Default name: required, 1 to 200 characters after trimming.</summary>
    public static IRuleBuilderOptions<T, string?> LocalizedName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is not null && value.Trim().Length is >= 1 and <= MaxLength)
            .WithMessage("'{PropertyName}' is required and must be 1 to 200 characters.");

    /// <summary>
    /// Translations: optional. Each bad key is its own error keyed <c>translations.&lt;code&gt;</c>: the <c>en</c> key gets
    /// <see cref="DefaultLanguageKeyCode"/> (English lives in <c>name</c>), a language that is not enabled gets
    /// <see cref="LanguageNotEnabledCode"/>; each non-empty value is at most 200 characters after trimming.
    /// </summary>
    public static IRuleBuilderInitial<T, IReadOnlyDictionary<string, string?>?> LocalizedTranslations<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string?>?> rule) =>
        rule.Custom((translations, context) =>
        {
            if (translations is null)
            {
                return;
            }

            foreach (var (key, text) in translations)
            {
                var code = key?.Trim().ToLowerInvariant() ?? string.Empty;
                var field = context.PropertyPath + "." + code;
                if (code == DefaultLanguage)
                {
                    context.AddFailure(new ValidationFailure(field, "English is the default language: send it as 'name'.") { ErrorCode = DefaultLanguageKeyCode });
                }
                else if (code != Arabic)
                {
                    context.AddFailure(new ValidationFailure(field, "This language is not enabled.") { ErrorCode = LanguageNotEnabledCode });
                }
                else if (text is not null && text.Trim().Length > MaxLength)
                {
                    context.AddFailure(field, "A translation must be at most 200 characters.");
                }
            }
        });

    public static IReadOnlyList<string> UnsupportedLanguages(IReadOnlyDictionary<string, string?> translations) =>
        [.. translations.Keys.Where(key => !string.Equals(key?.Trim(), Arabic, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The two stored columns for a validated name and translations.</summary>
    public static (string NameEn, string NameAr) ToStorage(string name, IReadOnlyDictionary<string, string?>? translations)
    {
        var arabic = translations?.FirstOrDefault(pair => string.Equals(pair.Key?.Trim(), Arabic, StringComparison.OrdinalIgnoreCase)).Value;
        return (name.Trim(), arabic?.Trim() ?? string.Empty);
    }

    /// <summary>The translations object of the API: only languages with a non-empty text.</summary>
    public static IReadOnlyDictionary<string, string> FromStorage(string nameAr) =>
        string.IsNullOrWhiteSpace(nameAr) ? new Dictionary<string, string>() : new Dictionary<string, string> { [Arabic] = nameAr.Trim() };
}
