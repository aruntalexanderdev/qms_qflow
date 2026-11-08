namespace Qms.Application.Auth;

/// <summary>One action of a module in the catalog: <c>Action</c> <c>view</c>, <c>Permission</c> <c>service-types.view</c>.</summary>
public sealed record PermissionActionInfo(string Action, string Permission);

/// <summary>One module of the catalog with its actions in display order.</summary>
public sealed record PermissionModuleInfo(string Module, IReadOnlyList<PermissionActionInfo> Actions);

/// <summary>
/// The description of the permission catalog (ADR-009 section 3) on top of the constants in <see cref="Permissions"/>: modules and
/// actions in display order (the order of <see cref="Permissions.All"/>) and the rule that every action needs the module's
/// <c>view</c>. Codes only: the web app translates <c>permissions.modules.&lt;module&gt;</c> and
/// <c>permissions.actions.&lt;action&gt;</c>.
/// </summary>
public static class PermissionCatalog
{
    public const string ViewAction = "view";

    private static readonly Dictionary<string, int> Positions = Permissions.All
        .Select((code, index) => (code, index))
        .ToDictionary(item => item.code, item => item.index, StringComparer.Ordinal);

    /// <summary>Modules in display order, each with its actions in display order.</summary>
    public static IReadOnlyList<PermissionModuleInfo> Modules { get; } =
    [
        .. Permissions.All
            .GroupBy(ModuleOf, StringComparer.Ordinal)
            .Select(group => new PermissionModuleInfo(
                group.Key,
                [.. group.Select(code => new PermissionActionInfo(ActionOf(code), code))])),
    ];

    /// <summary>True for a code that exists in the catalog (exact, case-sensitive).</summary>
    public static bool Contains(string? code) => code is not null && Positions.ContainsKey(code);

    /// <summary>Position of a code in the display order; <see cref="int.MaxValue"/> for an unknown code.</summary>
    public static int PositionOf(string code) => Positions.TryGetValue(code, out var position) ? position : int.MaxValue;

    /// <summary>The <c>view</c> permission of the module a code belongs to (<c>zones.edit</c> gives <c>zones.view</c>).</summary>
    public static string ViewOf(string code) => ModuleOf(code) + "." + ViewAction;

    public static string ModuleOf(string code) => code[..code.IndexOf('.', StringComparison.Ordinal)];

    public static string ActionOf(string code) => code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..];

    /// <summary>The codes sorted in display order (unknown codes last, ordinal).</summary>
    public static IReadOnlyList<string> InDisplayOrder(IEnumerable<string> codes) =>
        [.. codes.OrderBy(PositionOf).ThenBy(code => code, StringComparer.Ordinal)];
}
