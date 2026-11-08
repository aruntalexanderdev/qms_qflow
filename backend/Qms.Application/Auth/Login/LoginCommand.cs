using FluentValidation;
using Qms.Application.Common;

namespace Qms.Application.Auth;

public sealed record LoginCommand(string? UserName, string? Password, string? ClientIp = null);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    // The upper bounds stop huge passwords from costing hashing time.
    public LoginValidator()
    {
        RuleFor(command => command.UserName).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginHandler(IUserAccounts accounts, ITokenService tokens, IRefreshTokenStore refreshTokens)
    : ICommandHandler<LoginCommand, SessionTokens>
{
    public async Task<Result<SessionTokens>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var check = await accounts.CheckPasswordAsync(command.UserName!, command.Password!, cancellationToken);
        if (check.Outcome == PasswordCheckOutcome.LockedOut)
        {
            return AuthErrors.LockedOut;
        }

        if (check.Outcome != PasswordCheckOutcome.Succeeded || check.User is null)
        {
            return AuthErrors.InvalidCredentials;
        }

        var access = tokens.CreateAccessToken(check.User);
        var refresh = await refreshTokens.IssueAsync(check.User.Id, command.ClientIp, cancellationToken);
        return new SessionTokens(access.Value, access.ExpiresAt, refresh.Value, refresh.ExpiresAt, check.User);
    }
}
