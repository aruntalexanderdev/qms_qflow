namespace Qms.Application.Common;

/// <summary>
/// Opens an explicit cross-tenant section (ADR-003 Amendment 1, A.4). Only three callers may use it: the development
/// seeder, background jobs, and <see cref="CrossTenantDecorator{TCommand, TResult}"/> for handlers marked
/// <see cref="CrossTenantAttribute"/>. The code reviewer greps for <c>BeginCrossTenant</c>.
/// </summary>
public interface ITenantScope
{
    /// <summary>
    /// Starts a cross-tenant section. Disposing the handle ends it; sections nest, so disposing an inner handle
    /// does not end an outer one.
    /// </summary>
    IDisposable BeginCrossTenant();
}
