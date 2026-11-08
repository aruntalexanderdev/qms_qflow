using FluentValidation;
using FluentValidation.Results;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Domain.Enums;

namespace Qms.Application.CompanyRoles;

/// <summary>Validation shared by the create and update role commands (ADR-009 sections 2 and 3).</summary>
public static class RoleRules
{
    /// <summary>The scope as the API names it: exactly <c>Company</c> or <c>Assigned</c> (required).</summary>
    public static IRuleBuilderOptions<T, string?> BranchScopeName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is not null && Enum.GetNames<RoleBranchScope>().Contains(value, StringComparer.Ordinal))
            .WithMessage("'{PropertyName}' must be Company or Assigned.");

    public static RoleBranchScope ParseBranchScope(string? value) =>
        Enum.TryParse<RoleBranchScope>(value, ignoreCase: false, out var scope) && Enum.IsDefined(scope)
            ? scope
            : throw new InvalidOperationException("The branch scope was not validated.");

    /// <summary>
    /// The permission list: sent (may be empty); no blank or repeated code; every code in the catalog
    /// (<c>role.permission_unknown</c>); every code of a module comes with the module's <c>view</c> (<c>role.view_required</c>).
    /// When every failure has the same specific code, that code is the answer (see the validation decorator).
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, IReadOnlyList<string?>?> PermissionList<T>(this IRuleBuilder<T, IReadOnlyList<string?>?> rule) =>
        rule.Custom((codes, context) =>
        {
            if (codes is null)
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, "'Permissions' is required.") { ErrorCode = "NotNullValidator" });
                return;
            }

            if (codes.Any(string.IsNullOrWhiteSpace))
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, "A permission code must not be empty."));
                return;
            }

            if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, "A permission code must not be listed more than once."));
            }

            var known = codes.Where(PermissionCatalog.Contains).Select(code => code!).ToHashSet(StringComparer.Ordinal);
            if (codes.Any(code => !PermissionCatalog.Contains(code)))
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, "A permission code is not in the catalog.") { ErrorCode = RoleErrors.PermissionUnknownCode });
            }

            if (known.Any(code => !known.Contains(PermissionCatalog.ViewOf(code))))
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, "Every action needs the view permission of its module.") { ErrorCode = RoleErrors.ViewRequiredCode });
            }
        });

    /// <summary>The validated, distinct codes.</summary>
    public static HashSet<string> Codes(IReadOnlyList<string?>? permissions) =>
        new((permissions ?? []).Select(code => code!), StringComparer.Ordinal);
}
