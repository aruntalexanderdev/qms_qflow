namespace Qms.Application.Common;

/// <summary>
/// Marks a command whose success must write one <c>audit_log</c> row in the same transaction as the change
/// (spec section 11, ADR-008 section 11). <paramref name="entity"/> is the table-style name (<c>service_type</c>),
/// <paramref name="action"/> one of <c>create</c>, <c>update</c>, <c>delete</c> (or a specific verb). Queries are never audited.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AuditedAttribute(string entity, string action) : Attribute
{
    public string Entity { get; } = entity;

    public string Action { get; } = action;
}

/// <summary>
/// On a command property: the value is left out of the audit <c>data</c> (passwords, secrets). On a command type: the
/// command writes no audit row even if it also carries <see cref="AuditedAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NotAuditedAttribute : Attribute
{
}
