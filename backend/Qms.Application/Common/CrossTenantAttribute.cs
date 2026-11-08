namespace Qms.Application.Common;

/// <summary>
/// Marks a handler that works across companies. Only a Super Admin may call it: <see cref="CrossTenantDecorator{TCommand, TResult}"/>
/// returns <c>auth.forbidden</c> to anyone else, and opens the cross-tenant section for the handler call (ADR-003 Amendment 1).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CrossTenantAttribute : Attribute
{
}
