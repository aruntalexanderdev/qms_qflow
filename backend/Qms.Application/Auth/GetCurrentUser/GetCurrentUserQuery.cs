using Qms.Application.Common;

namespace Qms.Application.Auth;

public sealed record GetCurrentUserQuery;

/// <summary>Reads the user fresh from the database (name, language and assignments can change within the token's life).</summary>
public sealed class GetCurrentUserHandler(ICurrentUser currentUser, IUserAccounts accounts)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<Result<CurrentUserDto>> HandleAsync(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.Unauthorized;
        }

        var user = await accounts.FindActiveAsync(userId, cancellationToken);
        return user is null ? AuthErrors.Unauthorized : user;
    }
}
