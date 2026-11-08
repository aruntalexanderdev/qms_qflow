using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.ServiceTypes;

/// <summary>Service types of the current company; <c>IsActive</c> filters when given.</summary>
public sealed record ListServiceTypesQuery(bool? IsActive);

public sealed class ListServiceTypesHandler(IQmsDbContext db) : IQueryHandler<ListServiceTypesQuery, IReadOnlyList<ServiceTypeDto>>
{
    public async Task<Result<IReadOnlyList<ServiceTypeDto>>> HandleAsync(ListServiceTypesQuery query, CancellationToken cancellationToken)
    {
        var rows = db.ServiceTypes.AsNoTracking();
        if (query.IsActive is { } isActive)
        {
            rows = rows.Where(row => row.IsActive == isActive);
        }

        var items = await rows
            .OrderBy(row => row.SortOrder).ThenBy(row => row.Name).ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.NameTranslations, row.Prefix, row.TargetMinutes, row.SortOrder, row.IsActive, row.RowVersion })
            .ToListAsync(cancellationToken);

        return items
            .Select(row => ServiceTypeDto.From(row.Id, row.Name, row.NameTranslations, row.Prefix, row.TargetMinutes, row.SortOrder, row.IsActive, row.RowVersion))
            .ToList();
    }
}
