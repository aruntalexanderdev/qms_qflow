namespace Qms.Api.Auth;

/// <summary>
/// The refresh token cookie (ADR-004): <c>HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth</c>, 7 days.
/// The narrow path keeps it off every other request.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "qms_refresh";
    public const string Path = "/api/v1/auth";

    public static void Append(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    public static string? Read(HttpRequest request) => request.Cookies.TryGetValue(Name, out var value) ? value : null;

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
