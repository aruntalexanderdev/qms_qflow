namespace Qms.Application.Common;

/// <summary>
/// An expected failure. <paramref name="Code"/> is a stable string such as <c>token.service_full</c>
/// that web and Android translate. <paramref name="Details"/> holds field errors for validation failures.
/// <paramref name="Extensions"/> are extra members of the problem details body (for example the devices that block a change).
/// </summary>
public sealed record Error(
    string Code,
    ErrorType Type,
    string Message,
    IReadOnlyDictionary<string, string[]>? Details = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public const string ValidationFailedCode = "validation.failed";

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(code, ErrorType.Validation, message, details);

    public static Error NotFound(string code, string message) => new(code, ErrorType.NotFound, message);

    public static Error Conflict(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, ErrorType.Conflict, message, null, extensions);

    public static Error Forbidden(string code, string message) => new(code, ErrorType.Forbidden, message);

    public static Error Unauthorized(string code, string message) => new(code, ErrorType.Unauthorized, message);
}
