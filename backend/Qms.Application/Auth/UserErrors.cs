using Qms.Application.Common;

namespace Qms.Application.Auth;

public static class UserErrors
{
    public const string NotFoundCode = "user.not_found";

    /// <summary>
    /// Returned for a user id that does not exist and for one of another company, so the response does not reveal
    /// whether the user exists elsewhere (ADR-003 Amendment 1, B.3).
    /// </summary>
    public static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The user was not found.");
}
