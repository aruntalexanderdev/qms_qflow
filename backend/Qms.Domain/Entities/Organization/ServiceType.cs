namespace Qms.Domain.Entities;

/// <summary>What the customer comes for. One-letter upper-case prefix, unique per company.</summary>
public class ServiceType : TenantEntity
{
    /// <summary>Default-language (English) text, required.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public required string Prefix { get; set; }

    public int? TargetMinutes { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
