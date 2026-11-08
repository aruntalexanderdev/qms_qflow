namespace Qms.Application.Auth;

/// <summary>
/// Permission names, <c>&lt;module&gt;.&lt;action&gt;</c> in kebab-case (for example <c>service-types.view</c>). A module is a
/// setup page or API area; an action is <c>view</c>, <c>create</c>, <c>edit</c>, <c>delete</c>, later also <c>print</c>,
/// <c>approve</c>. Endpoints are protected with <c>[RequirePermission(...)]</c>, never with a role name. To add a module:
/// add its constants here, add them to <see cref="All"/> and to <see cref="DefaultRoles"/>, add a migration that grants them to the
/// system roles of every company, and put the attribute on the controller actions.
/// </summary>
public static class Permissions
{
    public static class ServiceTypes
    {
        public const string View = "service-types.view";
        public const string Create = "service-types.create";
        public const string Edit = "service-types.edit";
        public const string Delete = "service-types.delete";
    }

    public static class PriorityTypes
    {
        public const string View = "priority-types.view";
        public const string Create = "priority-types.create";
        public const string Edit = "priority-types.edit";
        public const string Delete = "priority-types.delete";
    }

    public static class ServiceMapping
    {
        public const string View = "service-mapping.view";
        public const string Edit = "service-mapping.edit";
    }

    public static class Branches
    {
        public const string View = "branches.view";
        public const string Create = "branches.create";
        public const string Edit = "branches.edit";
        public const string Delete = "branches.delete";
    }

    public static class BranchHours
    {
        public const string View = "branch-hours.view";
        public const string Edit = "branch-hours.edit";
    }

    public static class BranchSettings
    {
        public const string View = "branch-settings.view";
        public const string Edit = "branch-settings.edit";
    }

    public static class BranchFees
    {
        public const string View = "branch-fees.view";
        public const string Edit = "branch-fees.edit";
    }

    public static class BranchServiceMapping
    {
        public const string View = "branch-service-mapping.view";
        public const string Edit = "branch-service-mapping.edit";
    }

    public static class Zones
    {
        public const string View = "zones.view";
        public const string Create = "zones.create";
        public const string Edit = "zones.edit";
        public const string Delete = "zones.delete";
    }

    public static class Counters
    {
        public const string View = "counters.view";
        public const string Create = "counters.create";
        public const string Edit = "counters.edit";
        public const string Delete = "counters.delete";
    }

    public static class CounterServiceMapping
    {
        public const string View = "counter-service-mapping.view";
        public const string Edit = "counter-service-mapping.edit";
    }

    public static class ServiceFlows
    {
        public const string View = "service-flows.view";
        public const string Create = "service-flows.create";
        public const string Edit = "service-flows.edit";
        public const string Delete = "service-flows.delete";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Edit = "users.edit";
        public const string Delete = "users.delete";
        public const string ResetPassword = "users.reset-password";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Create = "roles.create";
        public const string Edit = "roles.edit";
        public const string Delete = "roles.delete";
    }

    public static class PasswordPolicy
    {
        public const string View = "password-policy.view";
        public const string Edit = "password-policy.edit";
    }

    public static class Devices
    {
        public const string View = "devices.view";
        public const string Create = "devices.create";
        public const string Edit = "devices.edit";
        public const string Delete = "devices.delete";
        public const string RegistrationCode = "devices.registration-code";
    }

    public static class DispenserSettings
    {
        public const string View = "dispenser-settings.view";
        public const string Edit = "dispenser-settings.edit";
    }

    public static class TicketTemplates
    {
        public const string View = "ticket-templates.view";
        public const string Create = "ticket-templates.create";
        public const string Edit = "ticket-templates.edit";
        public const string Delete = "ticket-templates.delete";
    }

    public static class CompanyLanguages
    {
        public const string View = "company-languages.view";
        public const string Edit = "company-languages.edit";
    }

    /// <summary>Every permission that exists (the M2 catalog of ADR-009 section 3 plus company-languages): 60 codes. The order of this list is the display order of the catalog (see <see cref="PermissionCatalog"/>).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ServiceTypes.View, ServiceTypes.Create, ServiceTypes.Edit, ServiceTypes.Delete,
        PriorityTypes.View, PriorityTypes.Create, PriorityTypes.Edit, PriorityTypes.Delete,
        ServiceMapping.View, ServiceMapping.Edit,
        Branches.View, Branches.Create, Branches.Edit, Branches.Delete,
        BranchHours.View, BranchHours.Edit,
        BranchSettings.View, BranchSettings.Edit,
        BranchFees.View, BranchFees.Edit,
        BranchServiceMapping.View, BranchServiceMapping.Edit,
        Zones.View, Zones.Create, Zones.Edit, Zones.Delete,
        Counters.View, Counters.Create, Counters.Edit, Counters.Delete,
        CounterServiceMapping.View, CounterServiceMapping.Edit,
        ServiceFlows.View, ServiceFlows.Create, ServiceFlows.Edit, ServiceFlows.Delete,
        Users.View, Users.Create, Users.Edit, Users.Delete, Users.ResetPassword,
        Roles.View, Roles.Create, Roles.Edit, Roles.Delete,
        PasswordPolicy.View, PasswordPolicy.Edit,
        Devices.View, Devices.Create, Devices.Edit, Devices.Delete, Devices.RegistrationCode,
        DispenserSettings.View, DispenserSettings.Edit,
        TicketTemplates.View, TicketTemplates.Create, TicketTemplates.Edit, TicketTemplates.Delete,
        CompanyLanguages.View, CompanyLanguages.Edit,
    ];
}
