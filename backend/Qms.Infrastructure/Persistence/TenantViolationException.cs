namespace Qms.Infrastructure.Persistence;

/// <summary>Why a save was refused (ADR-003 Amendment 1).</summary>
public enum TenantViolationReason
{
    /// <summary>A tenant write with no current company and no cross-tenant section (server set-up error).</summary>
    NoTenant,

    /// <summary>A write to a row of another company.</summary>
    OtherCompany,

    /// <summary>A tenant row's company was changed after insert.</summary>
    CompanyChanged,
}

/// <summary>
/// A save tried to write tenant data it may not write (ADR-003). The message names the entity type but never a value,
/// so it is safe to log; the API maps it to problem details without it.
/// </summary>
public sealed class TenantViolationException(TenantViolationReason reason, string message) : InvalidOperationException(message)
{
    public TenantViolationReason Reason { get; } = reason;
}
