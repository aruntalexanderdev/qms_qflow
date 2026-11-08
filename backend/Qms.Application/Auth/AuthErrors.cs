using Qms.Application.Common;

namespace Qms.Application.Auth;

/// <summary>Stable error codes of the auth endpoints; web and Android translate them (ADR-002, ADR-004).</summary>
public static class AuthErrors
{
    public const string InvalidCredentialsCode = "auth.invalid_credentials";
    public const string LockedOutCode = "auth.locked_out";
    public const string RefreshInvalidCode = "auth.refresh_invalid";
    public const string RefreshReusedCode = "auth.refresh_reused";
    public const string UnauthorizedCode = "auth.unauthorized";
    public const string ForbiddenCode = "auth.forbidden";

    /// <summary>Same error for an unknown user, an inactive user and a wrong password: no user enumeration.</summary>
    public static Error InvalidCredentials { get; } =
        Error.Unauthorized(InvalidCredentialsCode, "The user name or password is incorrect.");

    public static Error LockedOut { get; } =
        Error.Unauthorized(LockedOutCode, "The account is locked after too many failed sign-in attempts. Try again later.");

    public static Error RefreshInvalid { get; } =
        Error.Unauthorized(RefreshInvalidCode, "The session has expired. Sign in again.");

    public static Error RefreshReused { get; } =
        Error.Unauthorized(RefreshReusedCode, "The session is no longer valid. Sign in again.");

    public static Error Unauthorized { get; } =
        Error.Unauthorized(UnauthorizedCode, "Authentication is required.");

    public static Error Forbidden { get; } =
        Error.Forbidden(ForbiddenCode, "You are not allowed to do this.");
}
