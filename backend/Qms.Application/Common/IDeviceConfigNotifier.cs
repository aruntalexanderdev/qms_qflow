namespace Qms.Application.Common;

/// <summary>
/// What changed for devices: everything of <see cref="CompanyId"/> (service type or priority type names, flags, fees),
/// or one <see cref="BranchId"/>, or one <see cref="DeviceId"/>.
/// </summary>
public sealed record DeviceConfigChange(Guid CompanyId, Guid? BranchId = null, Guid? DeviceId = null);

/// <summary>
/// Tells devices their configuration changed (ADR-008 section 12). Handlers enqueue the call with
/// <see cref="IPostCommitActions"/>, so it runs only after the commit. M2 registers a no-op; STORY-021 sends the
/// SignalR event <c>DeviceConfigChanged</c>. Implementations never throw to the caller.
/// </summary>
public interface IDeviceConfigNotifier
{
    Task ConfigChangedAsync(DeviceConfigChange change, CancellationToken cancellationToken);
}

/// <summary>
/// For an <c>[Audited]</c> command that runs without a current company (a Super Admin in a cross-tenant section): names the
/// company the audit row belongs to. Implement it on the command or on the result; a current tenant always wins.
/// </summary>
public interface IAuditCompanyTarget
{
    Guid AuditCompanyId { get; }
}
