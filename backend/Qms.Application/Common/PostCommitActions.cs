using Microsoft.Extensions.Logging;

namespace Qms.Application.Common;

/// <summary>
/// Work a handler wants done only after its transaction committed, for example telling devices that their
/// configuration changed (ADR-008 section 12). <see cref="AuditDecorator{TCommand, TResult}"/> runs the queue after the
/// commit and drops it when the command fails, so nothing is announced for a change that did not happen.
/// </summary>
public interface IPostCommitActions
{
    void Enqueue(Func<CancellationToken, Task> action);
}

/// <summary>Scoped queue behind <see cref="IPostCommitActions"/>. A failing action is logged and never fails the request.</summary>
public sealed class PostCommitActions : IPostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    public void Discard() => _actions.Clear();

    public async Task RunAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var actions = _actions.ToArray();
        _actions.Clear();
        foreach (var action in actions)
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The change is committed; a notification failure must not turn it into an error response.
                LogFailed(logger, exception);
            }
        }
    }

    private static void LogFailed(ILogger logger, Exception exception) =>
        logger.LogWarning(exception, "A post-commit action failed.");
}
