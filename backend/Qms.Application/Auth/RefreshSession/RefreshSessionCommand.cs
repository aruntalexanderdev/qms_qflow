using FluentValidation;
using Qms.Application.Common;

namespace Qms.Application.Auth;

/// <summary>A missing token is not a validation error: it is an expired session (401 <c>auth.refresh_invalid</c>).</summary>
public sealed record RefreshSessionCommand(string? RefreshToken, string? ClientIp = null);

public sealed class RefreshSessionValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        RuleFor(command => command.RefreshToken).MaximumLength(256);
    }
}

public sealed class RefreshSessionHandler(
    IRefreshTokenStore refreshTokens,
    IUserAccounts accounts,
    ITokenService tokens) : ICommandHandler<RefreshSessionCommand, SessionTokens>
{
    public async Task<Result<SessionTokens>> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return AuthErrors.RefreshInvalid;
        }

        var rotation = await refreshTokens.RotateAsync(command.RefreshToken, command.ClientIp, cancellationToken);
        switch (rotation.Outcome)
        {
            case RefreshRotationOutcome.Reused:
                return AuthErrors.RefreshReused;
            case RefreshRotationOutcome.Rotated when rotation.Token is { } newToken:
                var user = await accounts.FindActiveAsync(rotation.UserId, cancellationToken);
                if (user is null)
                {
                    // The user was deactivated or deleted: end the session.
                    await refreshTokens.RevokeFamilyAsync(newToken.Value, cancellationToken);
                    return AuthErrors.RefreshInvalid;
                }

                var access = tokens.CreateAccessToken(user);
                return new SessionTokens(access.Value, access.ExpiresAt, newToken.Value, newToken.ExpiresAt, user);
            default:
                return AuthErrors.RefreshInvalid;
        }
    }
}
