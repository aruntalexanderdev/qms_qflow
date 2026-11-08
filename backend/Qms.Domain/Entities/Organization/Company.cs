namespace Qms.Domain.Entities;

/// <summary>The bank (tenant). Not a tenant row itself: the tenant filter matches `Id`.</summary>
public class Company : Entity
{
    /// <summary>Default-language (English) text, required.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public required string Code { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>ISO 4217 currency of fees, e.g. AED.</summary>
    public required string CurrencyCode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Default language code, a row of `language`. `en` in phase 1; no API changes it (ADR-010 section 1).</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Languages the company uses. Always contains <see cref="DefaultLanguage"/> (check constraint).</summary>
    public string[] EnabledLanguages { get; set; } = ["en"];
}
