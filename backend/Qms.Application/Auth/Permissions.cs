namespace Qms.Application.Auth;

/// <summary>
/// Permission names, <c>&lt;module&gt;.&lt;action&gt;</c> in kebab-case (for example <c>service-types.view</c>). A module is a
/// setup page or API area; an action is <c>view</c>, <c>create</c>, <c>edit</c>, <c>delete</c>, later also <c>print</c>,
/// <c>approve</c>. Endpoints are protected with <c>[RequirePermission(...)]</c>, never with a role name. To add a module:
/// add its constants here, add them to <see cref="All"/>, grant them in <see cref="RolePermissionMap"/>, and put the attribute
/// on the controller actions. The future role and permission story replaces the map with database-backed roles.
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

    public static class CompanyLanguages
    {
        public const string View = "company-languages.view";
        public const string Edit = "company-languages.edit";
    }

    /// <summary>Every permission that exists.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ServiceTypes.View, ServiceTypes.Create, ServiceTypes.Edit, ServiceTypes.Delete,
        PriorityTypes.View, PriorityTypes.Create, PriorityTypes.Edit, PriorityTypes.Delete,
        CompanyLanguages.View, CompanyLanguages.Edit,
    ];
}

/// <summary>
/// The fixed role to permission table that reproduces the ADR-008 section 9 matrix until roles become configurable:
/// Company Admin has every company setup permission (including <c>company-languages</c>, ADR-010), Branch Manager only
/// <c>view</c> of service types and priority types, Counter Staff and Super Admin none (Super Admin has no company).
/// </summary>
public static class RolePermissionMap
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static readonly Dictionary<string, IReadOnlySet<string>> Map = new(StringComparer.Ordinal)
    {
        [Roles.CompanyAdmin] = new HashSet<string>(Permissions.All, StringComparer.Ordinal),
        [Roles.BranchManager] = new HashSet<string>([Permissions.ServiceTypes.View, Permissions.PriorityTypes.View], StringComparer.Ordinal),
    };

    public static IReadOnlySet<string> For(string? role) => role is not null && Map.TryGetValue(role, out var set) ? set : None;
}

/// <summary>
/// Answers whether the signed-in user holds a permission. The one place that knows how permissions are granted; endpoints
/// and handlers only ask. Today backed by <see cref="RolePermissionMap"/>.
/// </summary>
public interface IPermissionResolver
{
    Task<bool> HasPermissionAsync(Common.ICurrentUser user, string permission, CancellationToken cancellationToken);
}
