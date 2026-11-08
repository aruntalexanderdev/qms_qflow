using Qms.Application.Auth;

namespace Qms.Application.Common;

/// <summary>
/// Wraps a handler marked <see cref="CrossTenantAttribute"/> (after validation in the decorator chain, ADR-002):
/// the caller must be a Super Admin, otherwise the result is Forbidden and the handler body does not run and no
/// cross-tenant section is opened. Otherwise the section is open for exactly the handler call.
/// </summary>
public sealed class CrossTenantDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantScope _tenantScope;

    public CrossTenantDecorator(ICommandHandler<TCommand, TResult> inner, ICurrentUser currentUser, ITenantScope tenantScope)
    {
        _inner = inner;
        _currentUser = currentUser;
        _tenantScope = tenantScope;
    }

    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.SuperAdmin))
        {
            return Result<TResult>.Failure(AuthErrors.Forbidden);
        }

        using (_tenantScope.BeginCrossTenant())
        {
            return await _inner.HandleAsync(command, cancellationToken);
        }
    }
}

/// <summary>Query version of <see cref="CrossTenantDecorator{TCommand, TResult}"/>.</summary>
public sealed class CrossTenantQueryDecorator<TQuery, TResult> : IQueryHandler<TQuery, TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _inner;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantScope _tenantScope;

    public CrossTenantQueryDecorator(IQueryHandler<TQuery, TResult> inner, ICurrentUser currentUser, ITenantScope tenantScope)
    {
        _inner = inner;
        _currentUser = currentUser;
        _tenantScope = tenantScope;
    }

    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.SuperAdmin))
        {
            return Result<TResult>.Failure(AuthErrors.Forbidden);
        }

        using (_tenantScope.BeginCrossTenant())
        {
            return await _inner.HandleAsync(query, cancellationToken);
        }
    }
}
