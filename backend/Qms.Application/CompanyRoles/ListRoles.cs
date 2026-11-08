using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation;

namespace Qms.Application.CompanyRoles;

/// <summary>Roles of the current company, by sort order then name; <c>IsActive</c> filters when given.</summary>
public sealed record ListRolesQuery(bool? IsActive);

public sealed class ListRolesHandler(IQmsDbContext db, ICompanyLanguages languages) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(ListRolesQuery query, CancellationToken cancellationToken)
    {
        var rows = RoleRow.Query(db);
        if (query.IsActive is { } isActive)
        {
            rows = rows.Where(row => row.IsActive == isActive);
        }

        var items = await rows.ToListAsync(cancellationToken);
        var company = await languages.GetCompanySettingsAsync(cancellationToken);
        return items.Select(row => row.ToDto(company)).ToList();
    }
}

public sealed record GetRoleQuery(Guid Id);

public sealed class GetRoleHandler(IQmsDbContext db, ICompanyLanguages languages) : IQueryHandler<GetRoleQuery, RoleDto>
{
    public async Task<Result<RoleDto>> HandleAsync(GetRoleQuery query, CancellationToken cancellationToken)
    {
        // The tenant filter hides other companies' roles: they are "not found", like an unknown id.
        var row = await RoleRow.Query(db).FirstOrDefaultAsync(role => role.Id == query.Id, cancellationToken);
        return row is null ? RoleErrors.NotFound : row.ToDto(await languages.GetCompanySettingsAsync(cancellationToken));
    }
}

/// <summary>The permission catalog in display order, codes only (ADR-009 section 3).</summary>
public sealed record ListPermissionsQuery;

public sealed class ListPermissionsHandler : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionModuleInfo>>
{
    public Task<Result<IReadOnlyList<PermissionModuleInfo>>> HandleAsync(ListPermissionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PermissionModuleInfo>>.Success(PermissionCatalog.Modules));
}
