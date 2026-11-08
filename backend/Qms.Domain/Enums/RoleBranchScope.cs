namespace Qms.Domain.Enums;

/// <summary>Which branches a role gives access to (ADR-009 section 2). Stored as text.</summary>
public enum RoleBranchScope
{
    /// <summary>Every branch of the company; no branch assignments needed.</summary>
    Company,

    /// <summary>Only the branches and counters assigned to the user (<c>user_branch</c>, <c>user_counter</c>).</summary>
    Assigned,
}
