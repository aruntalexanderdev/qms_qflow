using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Qms.Application.Auth;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// Access token: JWT, HMAC-SHA256, 15 minutes, claims <c>sub</c>, <c>company_id</c> (absent for a Super Admin),
/// <c>name</c>, <c>lang</c> and, for a Super Admin only, <c>role</c> (ADR-004, ADR-009 section 6): no permissions and no company
/// roles are in the token, the server resolves them per request. Time comes from <see cref="TimeProvider"/>.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    public static class ClaimNames
    {
        public const string Subject = "sub";
        public const string CompanyId = "company_id";
        public const string Role = "role";
        public const string Name = "name";
        public const string Language = "lang";
    }

    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(CurrentUserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [ClaimNames.Subject] = user.Id.ToString(),
            [ClaimNames.Name] = user.Name,
            [ClaimNames.Language] = user.Language,
        };
        if (user.IsSuperAdmin)
        {
            claims[ClaimNames.Role] = Roles.SuperAdmin;
        }

        if (user.CompanyId is { } companyId)
        {
            claims[ClaimNames.CompanyId] = companyId.ToString();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.SigningKeyBytes), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}
