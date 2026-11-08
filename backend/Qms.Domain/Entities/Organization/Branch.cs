namespace Qms.Domain.Entities;

/// <summary>A physical site. `Timezone` is an IANA id (e.g. Asia/Dubai) used for the business date.</summary>
public class Branch : TenantEntity
{
    /// <summary>Default-language (English) text, required.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public required string Code { get; set; }

    public required string Timezone { get; set; }

    public bool IsActive { get; set; } = true;
}
