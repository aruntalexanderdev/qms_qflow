using Qms.Application.Common;
using Qms.Domain.Entities;

namespace Qms.Application.ServiceTypes;

/// <summary>A service type as the API returns it (STORY-005, spec section 9 <c>service_type</c>). <c>Version</c> is opaque.</summary>
public sealed record ServiceTypeDto(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Translations,
    string Prefix,
    int? TargetMinutes,
    int SortOrder,
    bool IsActive,
    string Version)
{
    public static ServiceTypeDto From(ServiceType row) => From(
        row.Id, row.NameEn, row.NameAr, row.Prefix, row.TargetMinutes, row.SortOrder, row.IsActive, row.RowVersion);

    public static ServiceTypeDto From(
        Guid id, string nameEn, string nameAr, string prefix, int? targetMinutes, int sortOrder, bool isActive, uint rowVersion) =>
        new(id, nameEn, LocalizedText.FromStorage(nameAr), prefix.Trim(), targetMinutes, sortOrder, isActive, RowVersionCodec.Encode(rowVersion));
}

/// <summary>Stable error codes of the service type endpoints (web and Android translate them).</summary>
public static class ServiceTypeErrors
{
    public const string NotFoundCode = "service_type.not_found";
    public const string PrefixTakenCode = "service_type.prefix_taken";
    public const string InUseCode = "service_type.in_use";

    public static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The service type was not found.");

    public static Error PrefixTaken { get; } = Error.Conflict(PrefixTakenCode, "Another service type of the company already uses this prefix.");

    public static Error InUse { get; } = Error.Conflict(InUseCode, "The service type is in use. Deactivate it instead of deleting it.");
}
