using Qms.Application.Localisation;

namespace Qms.Tests.Support;

/// <summary>An <see cref="ICompanyLanguages"/> for unit tests: fixed registry and company setting, counts how often it is read.</summary>
public sealed class FakeCompanyLanguages(CompanyLanguageSettings? company = null, IReadOnlyList<LanguageInfo>? registry = null) : ICompanyLanguages
{
    public static IReadOnlyList<LanguageInfo> Registry { get; } =
    [
        new("en", "English", "ltr", true, 1),
        new("ar", "العربية", "rtl", true, 2),
    ];

    public static CompanyLanguageSettings EnglishAndArabic { get; } = new("en", ["en", "ar"]);

    public int Reads { get; private set; }

    public Task<LanguageSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(new LanguageSnapshot(registry ?? Registry, company ?? CompanyLanguageSettings.English));
    }
}
