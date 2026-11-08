namespace Qms.Infrastructure.Persistence;

/// <summary>A save tried to write a row that belongs to another company (ADR-003).</summary>
public sealed class TenantViolationException(string message) : InvalidOperationException(message);
