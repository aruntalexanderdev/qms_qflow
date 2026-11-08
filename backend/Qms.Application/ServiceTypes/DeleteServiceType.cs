using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.ServiceTypes;

[Audited("service_type", "delete")]
public sealed record DeleteServiceTypeCommand(Guid Id);

public sealed class DeleteServiceTypeHandler(IQmsDbContext db) : ICommandHandler<DeleteServiceTypeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteServiceTypeCommand command, CancellationToken cancellationToken)
    {
        // Load first: the DELETE then carries the row's own xmin (never delete through a stub entity).
        var row = await db.ServiceTypes.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (row is null)
        {
            return ServiceTypeErrors.NotFound;
        }

        if (await IsReferencedAsync(row.Id, cancellationToken))
        {
            return ServiceTypeErrors.InUse;
        }

        // A reference created after this check is stopped by the foreign keys (23503 / 23001, mapped to the same code).
        db.ServiceTypes.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }

    private async Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.CompanyServicePriorities.AnyAsync(m => m.ServiceTypeId == id, cancellationToken)
        || await db.BranchServicePriorities.AnyAsync(m => m.ServiceTypeId == id, cancellationToken)
        || await db.CounterServicePriorities.AnyAsync(m => m.ServiceTypeId == id, cancellationToken)
        || await db.ServiceFlows.AnyAsync(f => f.FromServiceTypeId == id || f.ToServiceTypeId == id, cancellationToken)
        || await db.DispenserZoneServices.AnyAsync(m => m.ServiceTypeId == id, cancellationToken)
        || await db.Tokens.AnyAsync(t => t.CurrentServiceTypeId == id, cancellationToken)
        || await db.TokenVisits.AnyAsync(v => v.ServiceTypeId == id, cancellationToken)
        || await db.TokenSequences.AnyAsync(s => s.ServiceTypeId == id, cancellationToken);
}
