using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Infrastructure.Identity;

namespace Qms.Api.Auth;

public static class AuthenticationSetup
{
    public const string LoginRateLimitPolicy = "login";

    /// <summary>
    /// JWT bearer authentication, the fallback policy (every endpoint needs a signed-in user unless it says
    /// <c>[AllowAnonymous]</c>), role policies, the login rate limiter, <see cref="ICurrentUser"/> and the claims-based
    /// tenant context (ADR-003 Amendment 1, N7; ADR-004).
    /// </summary>
    public static IServiceCollection AddQmsAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // One instance per request serves as both the tenant and the cross-tenant scope.
        services.AddScoped<RequestTenantContext>();
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<RequestTenantContext>());
        services.AddScoped<ITenantScope>(provider => provider.GetRequiredService<RequestTenantContext>());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var role in Roles.All)
            {
                options.AddPolicy(role, policy => policy.RequireAuthenticatedUser().RequireRole(role));
            }
        });

        // Permission-based authorization for the setup API: [RequirePermission] + one handler (later stories only add constants).
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, UnmatchedRouteAuthorizationResultHandler>();

        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>(ConfigureRateLimiter);
        return services;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions options, IConfiguration configuration)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        var permitLimit = configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
        var windowSeconds = configuration.GetValue("RateLimiting:Login:WindowSeconds", 60);

        // 10 sign-in attempts per minute per client address. Behind a reverse proxy the address must come from
        // forwarded headers (ADR-006 follow-up), otherwise all clients share one counter.
        options.AddPolicy(LoginRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
            }));

        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails =
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Extensions = { ["code"] = "request.rate_limited" },
                },
            });
        };
    }
}

/// <summary>Validates issuer, audience, lifetime and signing key; reads <c>access_token</c> from the query for SignalR (ADR-004).</summary>
public sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt) : IConfigureNamedOptions<JwtBearerOptions>
{
    private const string HubPathPrefix = "/hubs";

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var settings = jwt.Value;
        // Raw claim names (sub, role, ...) instead of the long WS-* URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),
            NameClaimType = JwtTokenService.ClaimNames.Name,
            RoleClaimType = JwtTokenService.ClaimNames.Role,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Browsers cannot set headers on a WebSocket: the SignalR client sends the token as access_token.
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    }
}

/// <summary>
/// The fallback policy also applies to requests that match no endpoint, which would turn an unknown route into 401.
/// With no endpoint there is nothing to protect, so those requests continue and keep their plain 404 problem details.
/// Every real endpoint is still protected by default.
/// </summary>
public sealed class UnmatchedRouteAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult) =>
        context.GetEndpoint() is null
            ? next(context)
            : _default.HandleAsync(next, context, policy, authorizeResult);
}
