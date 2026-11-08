using Microsoft.AspNetCore.Mvc.ModelBinding;
using Qms.Api.Auth;
using Qms.Api.Common;
using Qms.Application.Common;
using DependencyInjection = Qms.Infrastructure.DependencyInjection;
using Qms.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// preserveStaticLogger: each host keeps its own logger, so several hosts in one process (tests) stay isolated.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext(),
    preserveStaticLogger: true);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        // One key style for clients: camelCase, no "$." prefix (same as FluentValidation errors).
        var normalized = new ModelStateDictionary();
        foreach (var (key, entry) in context.ModelState)
        {
            foreach (var error in entry.Errors)
            {
                normalized.AddModelError(ErrorKeyNaming.Normalize(key), error.ErrorMessage);
            }
        }

        context.ModelState.Clear();
        foreach (var (key, entry) in normalized)
        {
            foreach (var error in entry.Errors)
            {
                context.ModelState.AddModelError(key, error.ErrorMessage);
            }
        }

        return defaultFactory(context);
    };
});
builder.Services.AddQmsProblemDetails();
builder.Services.AddQmsAuthentication();
builder.Services.AddQmsHealthChecks();
builder.Services.AddQmsOpenApi();

var app = builder.Build();

// Fail fast, before serving traffic, when the database is not configured.
DependencyInjection.GetRequiredConnectionString(app.Configuration);

// Development (and test hosts) only: apply migrations and the example data. Production/Azure use the migration
// bundle (ADR-006) and never seed. "Database:MigrateOnStartup" lets a host opt out (tests without a database).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await DependencyInjection.MigrateAndSeedAsync(app.Services, app.Lifetime.ApplicationStopping);
}

app.UseMiddleware<CorrelationIdMiddleware>();
// Use the host logger, not the static Log.Logger, so several hosts in one process (tests) stay isolated.
app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Bank QMS API v1");
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapQmsHealthChecks();

await app.RunAsync();

/// <summary>Entry point type, public so integration tests can start the host.</summary>
public partial class Program
{
}
