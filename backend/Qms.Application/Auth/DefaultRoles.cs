using Qms.Domain.Entities;
using Qms.Domain.Enums;

namespace Qms.Application.Auth;

/// <summary>One default system role: its definition and its default grants (ADR-009 section 4).</summary>
public sealed record DefaultRoleDefinition(
    SystemRoleCode Code, string Name, RoleBranchScope BranchScope, int SortOrder, IReadOnlyList<string> Permissions);

/// <summary>
/// The three system roles every company gets, with the default grants of ADR-009 section 4 (Company Admin 60, Branch Manager /
/// Supervisor 28, Counter Staff 0). Used by the dev seeder, the test data and, later, company creation (STORY-047). The
/// ConfigurableRoles migration writes the same rows as literal SQL for the companies that already exist (a migration never calls
/// code that changes); a test keeps both in step. A story that adds a module adds its codes here and a migration that grants them.
/// </summary>
public static class DefaultRoles
{
    public static IReadOnlyList<DefaultRoleDefinition> Definitions { get; } =
    [
        new(SystemRoleCode.CompanyAdmin, "Company Admin", RoleBranchScope.Company, 1, Permissions.All),
        new(SystemRoleCode.BranchManager, "Branch Manager / Supervisor", RoleBranchScope.Assigned, 2,
        [
            // view only
            Permissions.ServiceTypes.View, Permissions.PriorityTypes.View, Permissions.ServiceMapping.View,
            Permissions.TicketTemplates.View, Permissions.Branches.View, Permissions.BranchHours.View,
            Permissions.BranchSettings.View, Permissions.BranchFees.View, Permissions.ServiceFlows.View,

            // all actions (own branches)
            Permissions.BranchServiceMapping.View, Permissions.BranchServiceMapping.Edit,
            Permissions.Zones.View, Permissions.Zones.Create, Permissions.Zones.Edit, Permissions.Zones.Delete,
            Permissions.Counters.View, Permissions.Counters.Create, Permissions.Counters.Edit, Permissions.Counters.Delete,
            Permissions.CounterServiceMapping.View, Permissions.CounterServiceMapping.Edit,
            Permissions.Devices.View, Permissions.Devices.Create, Permissions.Devices.Edit, Permissions.Devices.Delete, Permissions.Devices.RegistrationCode,
            Permissions.DispenserSettings.View, Permissions.DispenserSettings.Edit,
        ]),
        new(SystemRoleCode.CounterStaff, "Counter Staff", RoleBranchScope.Assigned, 3, []),
    ];

    /// <summary>The definition of one system role.</summary>
    public static DefaultRoleDefinition For(SystemRoleCode code) => Definitions.Single(definition => definition.Code == code);

    /// <summary>
    /// New (untracked) system role rows with their grants for a company: active, <c>IsSystem</c>, English names, no translations.
    /// The caller adds them to the context and saves.
    /// </summary>
    public static IReadOnlyList<Role> CreateFor(Guid companyId) =>
    [
        .. Definitions.Select(definition => new Role
        {
            CompanyId = companyId,
            Name = definition.Name,
            BranchScope = definition.BranchScope,
            IsSystem = true,
            SystemCode = definition.Code,
            IsActive = true,
            SortOrder = definition.SortOrder,
            Permissions = [.. definition.Permissions.Select(permission => new RolePermission { CompanyId = companyId, Permission = permission })],
        }),
    ];
}
