using System.Text.Json.Serialization;
using Qms.Application.Common;
using Qms.Application.Localisation.Languages;
using Qms.Domain.Entities;

namespace Qms.Application.Localisation.CompanyLanguages;

/// <summary>
/// The language setting of the current company (spec section 10, ADR-010 section 3). <c>Version</c> is the company row
/// version (opaque). <c>Id</c> (the company) is not sent to clients: it is the entity id of the audit row.
/// </summary>
public sealed record CompanyLanguagesDto(
    string DefaultLanguage,
    IReadOnlyList<string> EnabledLanguages,
    IReadOnlyList<LanguageDto> AvailableLanguages,
    string Version)
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public static CompanyLanguagesDto From(Company company, IReadOnlyList<LanguageInfo> registry) =>
        new(
            company.DefaultLanguage.Trim(),
            [.. company.EnabledLanguages.InRegistryOrder(registry)],
            [.. registry.Where(language => language.IsAvailable).OrderBy(language => language.SortOrder).Select(LanguageDto.From)],
            RowVersionCodec.Encode(company.RowVersion))
        {
            Id = company.Id,
        };
}

/// <summary>Stable error codes of the company language endpoints (web and Android translate them).</summary>
public static class CompanyLanguageErrors
{
    public static Error NotAvailable(IReadOnlyCollection<string> codes) =>
        Error.Validation(
            ErrorCodes.CompanyLanguageNotAvailable,
            "A language is not available in this release.",
            new Dictionary<string, string[]> { ["enabledLanguages"] = [.. codes.Select(code => $"The language '{Shorten(code)}' is not available.")] });

    public static Error DefaultRequired(string defaultLanguage) =>
        Error.Validation(
            ErrorCodes.CompanyLanguageDefaultRequired,
            "The default language of the company must stay enabled.",
            new Dictionary<string, string[]> { ["enabledLanguages"] = [$"The default language '{defaultLanguage}' must be enabled."] });

    public static Error InUse(IReadOnlyCollection<string> languages, IReadOnlyCollection<DispenserUsingLanguage> devices) =>
        Error.Conflict(
            ErrorCodes.CompanyLanguageInUse,
            "A token dispenser still offers a language you are disabling. Remove it from the dispenser first.",
            new Dictionary<string, object?> { ["languages"] = languages, ["devices"] = devices });

    // Echoes at most a registry-sized code, never a long caller text.
    private static string Shorten(string code) => code.Length <= 35 ? code : code[..35];
}

/// <summary>A token dispenser that lists a language (the 409 <c>company_language.in_use</c> answer).</summary>
public sealed record DispenserUsingLanguage(Guid Id, string Name);
