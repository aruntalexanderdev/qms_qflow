namespace Qms.Application.Auth;

/// <summary>Role names (spec section 8). One role per user; the value of the <c>role</c> claim.</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CompanyAdmin = "CompanyAdmin";

    /// <summary>Branch Manager / Supervisor.</summary>
    public const string BranchManager = "BranchManager";
    public const string CounterStaff = "CounterStaff";

    public static IReadOnlyList<string> All { get; } = [SuperAdmin, CompanyAdmin, BranchManager, CounterStaff];
}
