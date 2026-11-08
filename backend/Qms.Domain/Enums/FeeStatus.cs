namespace Qms.Domain.Enums;

/// <summary>State of the paid-priority (VIP) fee on a token (spec section 6).</summary>
public enum FeeStatus
{
    None,
    Due,
    Paid,
    Waived,
}
