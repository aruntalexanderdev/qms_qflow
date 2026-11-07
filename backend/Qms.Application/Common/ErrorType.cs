namespace Qms.Application.Common;

/// <summary>Kind of expected failure; the API maps each value to an HTTP status (ADR-002).</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
}
