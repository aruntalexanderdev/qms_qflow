using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Qms.Tests.Support;

public sealed class InMemorySink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    /// <summary>Waits until a matching event is logged (request logging runs after the response headers are sent).</summary>
    public async Task<IReadOnlyList<LogEvent>> WaitForAsync(Func<LogEvent, bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var matches = Events.Where(predicate).ToList();
            if (matches.Count > 0)
            {
                return matches;
            }

            await Task.Delay(50);
        }

        return [];
    }

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
