using Serilog.Context;

namespace Qms.Api.Common;

/// <summary>
/// Reads <c>X-Correlation-Id</c> (or creates one), echoes it on the response and pushes it into the log context
/// so every log line of the request carries <c>CorrelationId</c>.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].ToString());
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await _next(context);
        }
    }

    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;

    // Incoming values are untrusted (they end up in logs and headers): accept only short, safe tokens.
    private static string ResolveCorrelationId(string? incoming) =>
        !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= MaxLength && incoming.All(IsSafe)
            ? incoming
            : Guid.NewGuid().ToString("N");

    private static bool IsSafe(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
