using Qms.Api.Common;
using Qms.Application.Common;
using Qms.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddQmsProblemDetails();
builder.Services.AddQmsHealthChecks();
builder.Services.AddQmsOpenApi();

var app = builder.Build();

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
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Bank QMS API v1");
    });
}

app.MapControllers();
app.MapQmsHealthChecks();

await app.RunAsync();

/// <summary>Entry point type, public so integration tests can start the host.</summary>
public partial class Program
{
}
