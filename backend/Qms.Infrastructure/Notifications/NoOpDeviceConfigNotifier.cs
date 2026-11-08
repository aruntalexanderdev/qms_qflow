using Qms.Application.Common;

namespace Qms.Infrastructure.Notifications;

/// <summary>
/// Milestone M2 registration of <see cref="IDeviceConfigNotifier"/>: does nothing and never throws. STORY-021 replaces it
/// with the SignalR <c>DeviceConfigChanged</c> event (ADR-008 section 12).
/// </summary>
public sealed class NoOpDeviceConfigNotifier : IDeviceConfigNotifier
{
    public Task ConfigChangedAsync(DeviceConfigChange change, CancellationToken cancellationToken) => Task.CompletedTask;
}
