using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.PriorityTypes;

public sealed record GetPriorityTypeQuery(Guid Id);

public sealed class GetPriorityTypeHandler(IQmsDbContext db) : IQueryHandler<GetPriorityTypeQuery, PriorityTypeDto>
{
    public async Task<Result<PriorityTypeDto>> HandleAsync(GetPriorityTypeQuery query, CancellationToken cancellationToken)
    {
        var row = await db.PriorityTypes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken);
        return row is null
            ? PriorityTypeErrors.NotFound
            : PriorityTypeDto.From(row, await PriorityTypeDto.CurrencyOfCompanyAsync(db, cancellationToken));
    }
}
