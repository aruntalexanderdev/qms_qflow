namespace Qms.Application.Common;

/// <summary>
/// Stable error codes shared by all setup endpoints, <c>&lt;area&gt;.&lt;reason&gt;</c> in snake_case (ADR-008 section 13).
/// Web and Android translate every code (en + ar). Per-entity codes (<c>service_type.prefix_taken</c>) live next to
/// their feature.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = Error.ValidationFailedCode;
    public const string RequestConflict = "request.conflict";
    public const string RequestInvalid = "request.invalid";
    public const string ResourceNotFound = "resource.not_found";
    public const string ResourceInUse = "resource.in_use";
    public const string ResourceDuplicate = "resource.duplicate";
    public const string AuthForbidden = "auth.forbidden";
    public const string TranslationLanguageNotEnabled = "translation.language_not_enabled";
    public const string TranslationDefaultLanguageKey = "translation.default_language_key";
    public const string CompanyLanguageNotAvailable = "company_language.not_available";
    public const string CompanyLanguageDefaultRequired = "company_language.default_required";
    public const string CompanyLanguageInUse = "company_language.in_use";
    public const string LanguageNotEnabled = "language.not_enabled";
}

/// <summary>Shared errors.</summary>
public static class CommonErrors
{
    /// <summary>The row changed since the client read it (stale <c>version</c>).</summary>
    public static Error Conflict { get; } =
        Error.Conflict(ErrorCodes.RequestConflict, "The data was changed by someone else. Reload and try again.");

    public static Error ResourceNotFound { get; } =
        Error.NotFound(ErrorCodes.ResourceNotFound, "The requested resource was not found.");
}
