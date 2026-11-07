using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Qms.Api.Common;

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddQmsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                problem.Extensions["traceId"] = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
                    ?? context.HttpContext.TraceIdentifier;
                problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext);
                if (!problem.Extensions.ContainsKey("code"))
                {
                    problem.Extensions["code"] = DefaultCode(problem.Status ?? context.HttpContext.Response.StatusCode);
                }
            });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    private static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "request.invalid",
        StatusCodes.Status401Unauthorized => "auth.unauthorized",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "resource.not_found",
        StatusCodes.Status405MethodNotAllowed => "request.method_not_allowed",
        StatusCodes.Status409Conflict => "request.conflict",
        StatusCodes.Status415UnsupportedMediaType => "request.unsupported_media_type",
        >= 500 => "server.unexpected_error",
        _ => "request.error",
    };
}

/// <summary>Turns unexpected exceptions into a 500 problem details response without leaking details.</summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path.Value);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, string? path);
}
