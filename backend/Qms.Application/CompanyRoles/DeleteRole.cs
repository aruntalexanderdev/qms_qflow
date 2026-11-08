using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.CompanyRoles;

/// <summary>Deletes a custom role no user holds. A system role is 409 <c>role.system</c>, a held role 409 <c>role.in_use</c>.</summary>
[Audited("role", "delete")]
public sealed record DeleteRoleCommand(Guid Id);

public sealed class DeleteRoleHandler(IQmsDbContext db) : ICommandHandler<DeleteRoleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        // Load first: the DELETE then carries the row's own xmin (never delete through a stub entity).
        var role = await db.CompanyRoles.FirstOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleErrors.System;
        }

        // A user_role row created after this check is stopped by the restrict foreign key (23001, mapped to the same code).
        if (await db.CompanyUserRoles.AnyAsync(userRole => userRole.RoleId == role.Id, cancellationToken))
        {
            return RoleErrors.InUse;
        }

        // role_permission rows go with the role (database cascade).
        db.CompanyRoles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
