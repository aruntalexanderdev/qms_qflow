using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Domain.Entities;

namespace Qms.Application.CompanyRoles;

/// <summary>
/// Creates a custom role (never a system role). The company comes from the token. Only <c>IsActive</c> (true) and
/// <c>SortOrder</c> (0) default; <c>Name</c>, <c>BranchScope</c> and <c>Permissions</c> must be sent (ADR-008 Amendment 3 A).
/// </summary>
[Audited("role", "create")]
public sealed record CreateRoleCommand(
    string? Name,
    IReadOnlyDictionary<string, string?>? Translations,
    string? BranchScope,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<string?>? Permissions);

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator(ICompanyLanguages languages)
    {
        RuleFor(command => command.Name).DefaultValue();
        RuleFor(command => command.Translations).Translations(languages);
        RuleFor(command => command.BranchScope).BranchScopeName();
        RuleFor(command => command.SortOrder).SortOrder();
        RuleFor(command => command.Permissions).PermissionList();
    }
}

public sealed class CreateRoleHandler(IQmsDbContext db, ICurrentTenant tenant, ICompanyLanguages languages) : ICommandHandler<CreateRoleCommand, RoleDto>
{
    public async Task<Result<RoleDto>> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("A role was created with no current company.");
        var name = command.Name!.Trim();

        // Friendly answer first; the unique index (company_id, lower(name)) stays the backstop for a race (mapped to the same code).
        if (await NameTakenAsync(name, null, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        var company = await languages.GetCompanySettingsAsync(cancellationToken);
        var role = new Role
        {
            CompanyId = companyId,
            Name = name,
            NameTranslations = LocalizedTextRules.Merge(null, command.Translations, company),
            BranchScope = RoleRules.ParseBranchScope(command.BranchScope),
            IsSystem = false,
            SystemCode = null,
            IsActive = command.IsActive,
            SortOrder = command.SortOrder,
            Permissions = [.. RoleRules.Codes(command.Permissions).Select(code => new RolePermission { CompanyId = companyId, Permission = code })],
        };
        db.CompanyRoles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        var row = await RoleRow.Query(db).SingleAsync(item => item.Id == role.Id, cancellationToken);
        return row.ToDto(company);
    }

    private async Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lower = name.ToLowerInvariant();
        return await db.CompanyRoles.AnyAsync(role => role.Name.ToLower() == lower && role.Id != exceptId, cancellationToken);
    }
}
