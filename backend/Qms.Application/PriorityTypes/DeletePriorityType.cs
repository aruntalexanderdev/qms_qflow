using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.PriorityTypes;

[Audited("priority_type", "delete")]
public sealed record DeletePriorityTypeCommand(Guid Id);

public sealed class DeletePriorityTypeHandler(IQmsDbContext db) : ICommandHandler<DeletePriorityTypeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeletePriorityTypeCommand command, CancellationToken cancellationToken)
    {
        var row = await db.PriorityTypes.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (row is null)
        {
            return PriorityTypeErrors.NotFound;
        }

        if (await IsReferencedAsync(row.Id, cancellationToken))
        {
            return PriorityTypeErrors.InUse;
        }

        db.PriorityTypes.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }

    private async Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.CompanyServicePriorities.AnyAsync(m => m.PriorityTypeId == id, cancellationToken)
        || await db.BranchServicePriorities.AnyAsync(m => m.PriorityTypeId == id, cancellationToken)
        || await db.CounterServicePriorities.AnyAsync(m => m.PriorityTypeId == id, cancellationToken)
        || await db.BranchPriorityFees.AnyAsync(f => f.PriorityTypeId == id, cancellationToken)
        || await db.Tokens.AnyAsync(t => t.PriorityTypeId == id, cancellationToken)
        || await db.TokenVisits.AnyAsync(v => v.PriorityTypeId == id, cancellationToken)
        || await db.TokenSequences.AnyAsync(s => s.PriorityTypeId == id, cancellationToken);
}
