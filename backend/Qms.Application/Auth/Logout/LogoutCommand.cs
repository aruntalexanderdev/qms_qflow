using FluentValidation;
using Qms.Application.Common;

namespace Qms.Application.Auth;

public sealed record LogoutCommand(string? RefreshToken);

public sealed class LogoutValidator : AbstractValidator<LogoutCommand>
{
    public LogoutValidator()
    {
        RuleFor(command => command.RefreshToken).MaximumLength(256);
    }
}

/// <summary>Revokes the whole refresh token family. Idempotent: no cookie, or an unknown one, is still a success.</summary>
public sealed class LogoutHandler(IRefreshTokenStore refreshTokens) : ICommandHandler<LogoutCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            await refreshTokens.RevokeFamilyAsync(command.RefreshToken, cancellationToken);
        }

        return true;
    }
}
