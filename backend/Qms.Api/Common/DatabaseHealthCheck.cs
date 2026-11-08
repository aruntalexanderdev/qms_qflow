using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qms.Infrastructure.Persistence;

namespace Qms.Api.Common;

/// <summary>Readiness check: PostgreSQL accepts a connection. A small custom check, so no extra package is needed.</summary>
public sealed class DatabaseHealthCheck(QmsDbContext db) : IHealthCheck
{
    public const string Name = "postgresql";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to PostgreSQL.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never put the exception message in the response: it can contain host names.
            return HealthCheckResult.Unhealthy("PostgreSQL check failed.", exception);
        }
    }
}
