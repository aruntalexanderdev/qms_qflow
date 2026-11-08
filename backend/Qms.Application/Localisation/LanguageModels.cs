namespace Qms.Application.Localisation;

/// <summary>Fixed values of phase 1 (ADR-010 section 1): English is the default language and the fallback.</summary>
public static class LanguageDefaults
{
    public const string DefaultLanguage = "en";
}

/// <summary>A row of the language registry (ADR-010 section 2). The registry is global, not tenant data.</summary>
public sealed record LanguageInfo(string Code, string NativeName, string Direction, bool IsAvailable, int SortOrder);

/// <summary>
/// The languages a company uses: its default language and the enabled ones (the default is always among them). Pure data,
/// so the localisation rules need no database.
/// </summary>
public sealed record CompanyLanguageSettings(string DefaultLanguage, IReadOnlyList<string> EnabledLanguages)
{
    /// <summary>Phase 1 setting, also the answer when a request has no company.</summary>
    public static CompanyLanguageSettings English { get; } = new(LanguageDefaults.DefaultLanguage, [LanguageDefaults.DefaultLanguage]);

    /// <summary>The enabled language that <paramref name="code"/> names (any letter case, padding ignored) in its registry form, else null.</summary>
    public string? Match(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var trimmed = code.Trim();
        return EnabledLanguages.FirstOrDefault(language => string.Equals(language, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsDefault(string? code) =>
        code is not null && string.Equals(DefaultLanguage, code.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the current request knows about languages: the registry and, for a company user, the company setting
/// (<see cref="Company"/> is null for an anonymous caller and for a Super Admin).
/// </summary>
public sealed record LanguageSnapshot(IReadOnlyList<LanguageInfo> Registry, CompanyLanguageSettings? Company);

/// <summary>
/// The languages of the current request, loaded with one query and memoised for the request (ADR-010 section 5, AC-26).
/// Validators and handlers all use this port. Implemented in Qms.Infrastructure.
/// </summary>
public interface ICompanyLanguages
{
    Task<LanguageSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>The language the response should use: <c>Accept-Language</c> matched against the languages on offer (AC-26). Implemented in Qms.Api.</summary>
public interface IRequestLanguage
{
    Task<string> GetCurrentAsync(CancellationToken cancellationToken);
}

public static class CompanyLanguagesExtensions
{
    /// <summary>The company setting of the request; English only when the request has no company.</summary>
    public static async Task<CompanyLanguageSettings> GetCompanySettingsAsync(this ICompanyLanguages languages, CancellationToken cancellationToken) =>
        (await languages.GetAsync(cancellationToken)).Company ?? CompanyLanguageSettings.English;

    /// <summary>The codes in registry order (unknown codes last, in the given order).</summary>
    public static IReadOnlyList<string> InRegistryOrder(this IEnumerable<string> codes, IReadOnlyList<LanguageInfo> registry)
    {
        var order = registry.ToDictionary(language => language.Code, language => language.SortOrder, StringComparer.OrdinalIgnoreCase);
        return [.. codes.Select((code, index) => (code, index))
            .OrderBy(item => order.TryGetValue(item.code, out var sort) ? sort : int.MaxValue)
            .ThenBy(item => item.index)
            .Select(item => item.code)];
    }
}
