using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Domain.Entities;

namespace Qms.Application.CompanyRoles;

/// <summary>
/// Replaces every editable field and the whole permission set (no partial update, ADR-008 Amendment 3 A). A system role keeps its
/// system code and cannot be deactivated (409 <c>role.system</c>). The change runs under the lock-out guard (ADR-009 section 7).
/// </summary>
[Audited("role", "update")]
public sealed record UpdateRoleCommand(
    Guid Id,
    string? Name,
    IReadOnlyDictionary<string, string?>? Translations,
    string? BranchScope,
    bool? IsActive,
    int? SortOrder,
    IReadOnlyList<string?>? Permissions,
    string? Version);

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator(ICompanyLanguages languages)
    {
        RuleFor(command => command.Name).DefaultValue();
        RuleFor(command => command.Translations).Translations(languages);
        RuleFor(command => command.BranchScope).BranchScopeName();
        RuleFor(command => command.IsActive).Sent();
        RuleFor(command => command.SortOrder).Sent();
        RuleFor(command => command.SortOrder).GreaterThanOrEqualTo(0).When(command => command.SortOrder is not null).WithMessage("'{PropertyName}' must be 0 or more.");
        RuleFor(command => command.Permissions).PermissionList();
        RuleFor(command => command.Version).RowVersionText();
    }
}

public sealed class UpdateRoleHandler(IQmsDbContext db, ICompanyLanguages languages, IAdminContinuityGuard continuity)
    : ICommandHandler<UpdateRoleCommand, RoleDto>
{
    public async Task<Result<RoleDto>> HandleAsync(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        // Lock the company first: concurrent role changes of one company run one after the other, and everything below reads their result.
        var check = await continuity.BeginAsync(cancellationToken);

        var role = await db.CompanyRoles.Include(item => item.Permissions).FirstOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        // A stale version is a conflict even when the new values equal the stored ones (no UPDATE would be sent then).
        var version = CommonRules.DecodeVersion(command.Version);
        if (role.RowVersion != version)
        {
            return CommonErrors.Conflict;
        }

        if (role.IsSystem && !command.IsActive!.Value)
        {
            return RoleErrors.System;
        }

        var name = command.Name!.Trim();
        var lower = name.ToLowerInvariant();
        if (!string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)
            && await db.CompanyRoles.AnyAsync(other => other.Name.ToLower() == lower && other.Id != role.Id, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        // The UPDATE then carries "AND xmin = version": a save racing between the load and here fails with a concurrency error (409).
        db.Entry(role).Property(item => item.RowVersion).OriginalValue = version;

        // Translations of languages that are not enabled stay as they are (ADR-010 section 5); the enabled ones are replaced.
        var company = await languages.GetCompanySettingsAsync(cancellationToken);
        role.Name = name;
        role.NameTranslations = LocalizedTextRules.Merge(role.NameTranslations, command.Translations, company);
        role.BranchScope = RoleRules.ParseBranchScope(command.BranchScope);
        role.IsActive = command.IsActive!.Value;
        role.SortOrder = command.SortOrder!.Value;

        var permissionsChanged = ReplacePermissions(role, RoleRules.Codes(command.Permissions));
        if (permissionsChanged && db.Entry(role).State == Microsoft.EntityFrameworkCore.EntityState.Unchanged)
        {
            // Only the permission rows changed, which would leave the role's xmin (the version) alone: touch the role row.
            db.Entry(role).Property(item => item.UpdatedAt).IsModified = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        var verdict = await check.VerifyAsync(cancellationToken);
        if (verdict.IsFailure)
        {
            // The audit decorator rolls the transaction back: nothing of the change is kept.
            return verdict.Error!;
        }

        var row = await RoleRow.Query(db).SingleAsync(item => item.Id == role.Id, cancellationToken);
        return row.ToDto(company);
    }

    /// <summary>Makes the stored set equal to <paramref name="wanted"/>; true when a row was added or removed.</summary>
    private bool ReplacePermissions(Role role, HashSet<string> wanted)
    {
        var changed = false;
        foreach (var existing in role.Permissions.Where(item => !wanted.Contains(item.Permission)).ToList())
        {
            db.RolePermissions.Remove(existing);
            changed = true;
        }

        var stored = role.Permissions.Select(item => item.Permission).ToHashSet(StringComparer.Ordinal);
        foreach (var code in wanted.Where(code => !stored.Contains(code)))
        {
            role.Permissions.Add(new RolePermission { CompanyId = role.CompanyId, RoleId = role.Id, Permission = code });
            changed = true;
        }

        return changed;
    }
}
