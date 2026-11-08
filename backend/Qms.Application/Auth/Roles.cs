namespace Qms.Application.Auth;

/// <summary>
/// Identity role names. Only <c>SuperAdmin</c> is an Identity role (and the value of the <c>role</c> claim, for a Super Admin
/// only); company roles are rows of the <c>role</c> table (ADR-009 section 1).
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";

    public static IReadOnlyList<string> All { get; } = [SuperAdmin];
}
