using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Domain.Entities;
using Qms.Domain.Enums;

namespace Qms.Application.CompanyRoles;

/// <summary>
/// A company role as the API returns it (STORY-048, spec section 9 <c>role</c>). <c>Permissions</c> are the codes still in the
/// catalog, in catalog display order; <c>UserCount</c> counts the <c>user_role</c> rows; <c>Version</c> is opaque and changes
/// when only the permission set changes.
/// </summary>
public sealed record RoleDto(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Translations,
    RoleBranchScope BranchScope,
    bool IsSystem,
    SystemRoleCode? SystemCode,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<string> Permissions,
    int UserCount,
    string Version);

/// <summary>The columns of a role as read from the database (a projection, no tracking).</summary>
internal sealed class RoleRow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Dictionary<string, string> NameTranslations { get; init; } = new();

    public RoleBranchScope BranchScope { get; init; }

    public bool IsSystem { get; init; }

    public SystemRoleCode? SystemCode { get; init; }

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }

    public List<string> Permissions { get; init; } = [];

    public int UserCount { get; init; }

    public uint RowVersion { get; init; }

    /// <summary>Roles of the current company (the tenant filter) with their permission codes and user count, in list order.</summary>
    public static IQueryable<RoleRow> Query(IQmsDbContext db) =>
        db.CompanyRoles.AsNoTracking()
            .OrderBy(role => role.SortOrder).ThenBy(role => role.Name).ThenBy(role => role.Id)
            .Select(role => new RoleRow
            {
                Id = role.Id,
                Name = role.Name,
                NameTranslations = role.NameTranslations,
                BranchScope = role.BranchScope,
                IsSystem = role.IsSystem,
                SystemCode = role.SystemCode,
                IsActive = role.IsActive,
                SortOrder = role.SortOrder,
                Permissions = role.Permissions.Select(permission => permission.Permission).ToList(),
                UserCount = db.CompanyUserRoles.Count(userRole => userRole.RoleId == role.Id),
                RowVersion = role.RowVersion,
            });

    public RoleDto ToDto(CompanyLanguageSettings languages) => new(
        Id,
        Name,
        LocalizedTextRules.ForAdmin(NameTranslations, languages),
        BranchScope,
        IsSystem,
        SystemCode,
        IsActive,
        SortOrder,
        PermissionCatalog.InDisplayOrder(Permissions.Where(PermissionCatalog.Contains)),
        UserCount,
        RowVersionCodec.Encode(RowVersion));
}

/// <summary>Stable error codes of the role endpoints (web and Android translate them).</summary>
public static class RoleErrors
{
    public const string NotFoundCode = "role.not_found";
    public const string NameTakenCode = "role.name_taken";
    public const string SystemCode = "role.system";
    public const string InUseCode = "role.in_use";
    public const string PermissionUnknownCode = "role.permission_unknown";
    public const string ViewRequiredCode = "role.view_required";
    public const string CannotChangeSelfCode = "role.cannot_change_self";
    public const string LastAdminCode = "role.last_admin";

    /// <summary>Also the answer for a role of another company (the tenant filter hides it).</summary>
    public static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The role was not found.");

    public static Error NameTaken { get; } = Error.Conflict(NameTakenCode, "Another role of the company already has this name.");

    public static Error System { get; } = Error.Conflict(SystemCode, "A system role cannot be deactivated or deleted.");

    public static Error InUse { get; } = Error.Conflict(InUseCode, "The role is held by users. Remove it from them first, or deactivate it.");

    public static Error CannotChangeSelf { get; } =
        Error.Conflict(CannotChangeSelfCode, "This change would remove your own right to manage roles or users.");

    public static Error LastAdmin { get; } =
        Error.Conflict(LastAdminCode, "The company must keep an active user who can manage roles and users with access to every branch.");
}
