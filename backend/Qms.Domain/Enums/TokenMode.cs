namespace Qms.Domain.Enums;

/// <summary>How a dispenser issues tokens (spec section 7). PhoneOnly is phase 2.</summary>
public enum TokenMode
{
    PrintOnly,
    PrintPhoneOptional,
    PrintPhoneRequired,
    PhoneOnly,
}
