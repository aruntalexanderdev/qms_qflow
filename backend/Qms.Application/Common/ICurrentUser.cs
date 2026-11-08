namespace Qms.Application.Common;

/// <summary>
/// The signed-in user of the request, read from the access token claims (ADR-003, ADR-004). Implemented in Qms.Api.
/// Actor columns (who issued, settled, served) are set only from <see cref="UserId"/>, never from a request body.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim.</summary>
    Guid? UserId { get; }

    /// <summary>The <c>company_id</c> claim; absent for a Super Admin.</summary>
    Guid? CompanyId { get; }

    /// <summary>True when the <c>role</c> claim is <see cref="Auth.Roles.SuperAdmin"/>. Only a Super Admin token carries a <c>role</c> claim (ADR-009 section 6).</summary>
    bool IsSuperAdmin { get; }

    /// <summary>The <c>lang</c> claim, <c>en</c> or <c>ar</c>.</summary>
    string? Language { get; }
}
