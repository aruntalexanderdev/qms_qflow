namespace Qms.Application.Common;

/// <summary>
/// Nest-counted cross-tenant flag shared by every <see cref="ITenantScope"/> implementation (the request context
/// in Qms.Api, the test doubles). The section is active while at least one handle is undisposed.
/// </summary>
public sealed class CrossTenantSection
{
    private int _depth;

    public bool IsActive => Volatile.Read(ref _depth) > 0;

    public IDisposable Begin()
    {
        Interlocked.Increment(ref _depth);
        return new Handle(this);
    }

    private sealed class Handle(CrossTenantSection owner) : IDisposable
    {
        private CrossTenantSection? _owner = owner;

        // Disposing twice must not end an outer section.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } section)
            {
                Interlocked.Decrement(ref section._depth);
            }
        }
    }
}
