using Microsoft.Extensions.Logging;

namespace Qms.Application.Common;

/// <summary>
/// Work a handler wants done only after its transaction committed, for example telling devices that their
/// configuration changed (ADR-008 section 12). <see cref="AuditDecorator{TCommand, TResult}"/> runs the queue after the
/// commit and drops it when the command fails, so nothing is announced for a change that did not happen.
/// </summary>
/// <remarks>
/// Only a command that goes through <see cref="AuditDecorator{TCommand, TResult}"/> (marked <c>[Audited]</c>, no
/// type-level <c>[NotAudited]</c>) has someone to run the queue. <see cref="Enqueue"/> throws
/// <see cref="InvalidOperationException"/> anywhere else, so an action can never be silently dropped. The actions run with
/// their own token (not the request's) and a timeout, so a client that disconnects after the commit does not skip them.
/// </remarks>
public interface IPostCommitActions
{
    void Enqueue(Func<CancellationToken, Task> action);
}

/// <summary>Scoped queue behind <see cref="IPostCommitActions"/>. A failing action is logged and never fails the request.</summary>
public sealed class PostCommitActions : IPostCommitActions
{
    /// <summary>Upper bound for running all actions of one command after the commit.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly List<Func<CancellationToken, Task>> _actions = [];
    private bool _runnerActive;

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_runnerActive)
        {
            throw new InvalidOperationException(
                "Post-commit actions can only be enqueued by an [Audited] command; nothing would run this action.");
        }

        _actions.Add(action);
    }

    /// <summary>Called by the audit decorator when the audited handler starts.</summary>
    public void BeginCollecting() => _runnerActive = true;

    public void Discard()
    {
        _actions.Clear();
        _runnerActive = false;
    }

    public async Task RunAsync(ILogger logger)
    {
        var actions = _actions.ToArray();
        _actions.Clear();
        _runnerActive = false;
        using var timeout = new CancellationTokenSource(Timeout);
        foreach (var action in actions)
        {
            try
            {
                await action(timeout.Token);
            }
            catch (Exception exception)
            {
                // The change is committed; a notification failure (or timeout) must not turn it into an error response.
                LogFailed(logger, exception);
            }
        }
    }

    private static void LogFailed(ILogger logger, Exception exception) =>
        logger.LogWarning(exception, "A post-commit action failed.");
}
