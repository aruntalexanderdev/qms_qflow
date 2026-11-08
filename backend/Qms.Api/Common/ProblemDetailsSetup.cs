using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qms.Infrastructure.Errors;
using Qms.Infrastructure.Persistence;

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

/// <summary>
/// Turns exceptions into problem details without leaking details (ADR-002, ADR-003 Amendment 1, C):
/// a concurrency conflict is 409 <c>request.conflict</c>; a database constraint violation (23001, 23503, 23505, 23514) is
/// mapped by constraint name through <see cref="PostgresErrorCatalog"/> (409 or 400 or 404 with a code, never the SQL message); a write to another company's row is 404 <c>resource.not_found</c>
/// (it does not confirm the row exists; Warning log, no entity values); a tenant write with no company is a server
/// set-up error, 500 <c>server.unexpected_error</c> (Error log); anything else is the same 500.
/// </summary>
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
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value;
        (int status, string title, string? code) outcome;
        switch (exception)
        {
            case DbUpdateConcurrencyException:
                LogConflict(method, path);
                outcome = (StatusCodes.Status409Conflict, "The data was changed by someone else. Reload and try again.", null);
                break;
            case DbUpdateException { InnerException: PostgresException postgres } when PostgresErrorCatalog.Map(postgres.SqlState, postgres.ConstraintName) is { } mapped:
                // Constraint name and SQLSTATE only: the message of the exception carries SQL and values.
                LogDatabaseError(postgres.SqlState, postgres.ConstraintName, method, path);
                outcome = (ResultExtensions.StatusFor(mapped.Type), "The request conflicts with existing data or is not valid.", mapped.Code);
                break;
            case TenantViolationException { Reason: TenantViolationReason.OtherCompany or TenantViolationReason.CompanyChanged } violation:
                // Not the exception itself: its message names the entity type. The reason is enough for the security log.
                LogTenantViolation(violation.Reason, method, path);
                outcome = (StatusCodes.Status404NotFound, "The requested resource was not found.", null);
                break;
            default:
                // Includes TenantViolationException NoTenant: the server is set up wrong, not the client.
                LogUnhandled(exception, method, path);
                outcome = (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null);
                break;
        }

        httpContext.Response.StatusCode = outcome.status;
        var problem = new ProblemDetails { Status = outcome.status, Title = outcome.title };
        if (outcome.code is not null)
        {
            problem.Extensions["code"] = outcome.code;
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database constraint {SqlState} {Constraint} for {Method} {Path}")]
    private partial void LogDatabaseError(string? sqlState, string? constraint, string method, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrency conflict for {Method} {Path}")]
    private partial void LogConflict(string method, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant violation ({Reason}) for {Method} {Path}")]
    private partial void LogTenantViolation(TenantViolationReason reason, string method, string? path);
}
