using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qms.Api.Auth;
using Qms.Api.Common;
using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Api.Controllers;

public sealed record LoginRequest(string? UserName, string? Password);

/// <summary>
/// The access token goes in the body (the web app keeps it in memory); the refresh token only in the cookie (ADR-004).
/// </summary>
public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAt, CurrentUserDto User);

/// <summary>Sign-in endpoints (spec section 10, ADR-004). Thin: bind, call the handler, set or clear the cookie.</summary>
[Route(ApiRoutes.Prefix + "/auth")]
public sealed class AuthController(TimeProvider timeProvider) : QmsControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        [FromServices] ICommandHandler<LoginCommand, SessionTokens> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.UserName, request.Password, ClientIp), cancellationToken);
        return SessionResponse(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromServices] ICommandHandler<RefreshSessionCommand, SessionTokens> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RefreshSessionCommand(RefreshCookie.Read(Request), ClientIp), cancellationToken);
        if (result.IsFailure)
        {
            // A rejected token is dead either way: do not keep sending it.
            RefreshCookie.Clear(Response);
        }

        return SessionResponse(result);
    }

    /// <summary>Anonymous on purpose: the refresh cookie is the credential, and the access token may have expired already.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromServices] ICommandHandler<LogoutCommand, bool> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LogoutCommand(RefreshCookie.Read(Request)), cancellationToken);
        RefreshCookie.Clear(Response);
        return result.IsSuccess ? NoContent() : result.Error!.ToProblem(this);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromServices] IQueryHandler<GetCurrentUserQuery, CurrentUserDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetCurrentUserQuery(), cancellationToken)).ToActionResult(this);

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult SessionResponse(Result<SessionTokens> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem(this);
        }

        var session = result.Value;
        RefreshCookie.Append(Response, session.RefreshToken, session.RefreshTokenExpiresAt);
        Response.Headers.CacheControl = "no-store";
        var expiresIn = (int)Math.Max(0, Math.Round((session.AccessTokenExpiresAt - timeProvider.GetUtcNow()).TotalSeconds));
        return Ok(new AuthResponse(session.AccessToken, "Bearer", expiresIn, session.AccessTokenExpiresAt, session.User));
    }
}
