using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Qms.Application.Common;

namespace Qms.Api.Common;

public static class ApplicationBuilderExtensions
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddQmsHealthChecks(this IServiceCollection services)
    {
        // "self" proves the process answers and has no dependencies (/health/live). Dependency checks are tagged "ready".
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag])
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, timeout: TimeSpan.FromSeconds(5), tags: [ReadyTag]);
        // No signed-in caller until STORY-003; the tenant filter therefore fails closed.
        services.AddScoped<ICurrentTenant, NoCompanyCurrentTenant>();
        return services;
    }

    public static IServiceCollection AddQmsOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "Bank QMS API", Version = "v1" };
                return Task.CompletedTask;
            }));
        return services;
    }

    public static WebApplication MapQmsHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains(LiveTag) })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();
        return app;
    }
}
