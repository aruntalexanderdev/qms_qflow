using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Domain.Entities;

namespace Qms.Application.PriorityTypes;

/// <summary>
/// A priority type as the API returns it (spec 4.1, 9 <c>priority_type</c>). <c>CurrencyCode</c> is the company currency,
/// read only. <c>FeeAmount</c> is a JSON number with at most two decimals, null when the type is not paid.
/// </summary>
public sealed record PriorityTypeDto(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Translations,
    string Prefix,
    int PriorityLevel,
    bool IsPaid,
    decimal? FeeAmount,
    string CurrencyCode,
    bool IsActive,
    string Version)
{
    /// <summary>The admin form: only the translations of the company's enabled languages are returned (BR-11).</summary>
    public static PriorityTypeDto From(PriorityType row, string currencyCode, CompanyLanguageSettings languages) => From(
        row.Id, row.Name, row.NameTranslations, row.Prefix, row.PriorityLevel, row.IsPaid, row.FeeAmount, currencyCode, row.IsActive, row.RowVersion, languages);

    public static PriorityTypeDto From(
        Guid id, string name, IReadOnlyDictionary<string, string> nameTranslations, string prefix, int priorityLevel, bool isPaid, decimal? feeAmount, string currencyCode, bool isActive, uint rowVersion,
        CompanyLanguageSettings languages) =>
        new(id, name, LocalizedTextRules.ForAdmin(nameTranslations, languages), prefix.Trim(), priorityLevel, isPaid, feeAmount, currencyCode.Trim(), isActive, RowVersionCodec.Encode(rowVersion));

    /// <summary>The currency of the current company (the tenant filter on <c>company</c> leaves exactly one row).</summary>
    internal static Task<string> CurrencyOfCompanyAsync(IQmsDbContext db, CancellationToken cancellationToken) =>
        db.Companies.AsNoTracking().Select(company => company.CurrencyCode).SingleAsync(cancellationToken);
}

/// <summary>Stable error codes of the priority type endpoints (web and Android translate them).</summary>
public static class PriorityTypeErrors
{
    public const string NotFoundCode = "priority_type.not_found";
    public const string PrefixTakenCode = "priority_type.prefix_taken";
    public const string InUseCode = "priority_type.in_use";

    public static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The priority type was not found.");

    public static Error PrefixTaken { get; } = Error.Conflict(PrefixTakenCode, "Another priority type of the company already uses this prefix.");

    public static Error InUse { get; } = Error.Conflict(InUseCode, "The priority type is in use. Deactivate it instead of deleting it.");
}
