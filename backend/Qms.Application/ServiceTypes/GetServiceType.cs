using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.ServiceTypes;

public sealed record GetServiceTypeQuery(Guid Id);

public sealed class GetServiceTypeHandler(IQmsDbContext db) : IQueryHandler<GetServiceTypeQuery, ServiceTypeDto>
{
    public async Task<Result<ServiceTypeDto>> HandleAsync(GetServiceTypeQuery query, CancellationToken cancellationToken)
    {
        // The tenant filter hides other companies' rows: they are "not found", like an unknown id.
        var row = await db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken);
        return row is null ? ServiceTypeErrors.NotFound : ServiceTypeDto.From(row);
    }
}
