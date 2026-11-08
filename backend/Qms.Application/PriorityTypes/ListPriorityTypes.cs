using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Application.Localisation;

namespace Qms.Application.PriorityTypes;

/// <summary>Priority types of the current company, highest priority first; <c>IsActive</c> filters when given.</summary>
public sealed record ListPriorityTypesQuery(bool? IsActive);

public sealed class ListPriorityTypesHandler(IQmsDbContext db, ICompanyLanguages languages) : IQueryHandler<ListPriorityTypesQuery, IReadOnlyList<PriorityTypeDto>>
{
    public async Task<Result<IReadOnlyList<PriorityTypeDto>>> HandleAsync(ListPriorityTypesQuery query, CancellationToken cancellationToken)
    {
        var rows = db.PriorityTypes.AsNoTracking();
        if (query.IsActive is { } isActive)
        {
            rows = rows.Where(row => row.IsActive == isActive);
        }

        var items = await rows
            .OrderBy(row => row.PriorityLevel).ThenBy(row => row.Prefix).ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.NameTranslations, row.Prefix, row.PriorityLevel, row.IsPaid, row.FeeAmount, row.IsActive, row.RowVersion })
            .ToListAsync(cancellationToken);
        var currency = await PriorityTypeDto.CurrencyOfCompanyAsync(db, cancellationToken);
        var company = await languages.GetCompanySettingsAsync(cancellationToken);

        return items
            .Select(row => PriorityTypeDto.From(
                row.Id, row.Name, row.NameTranslations, row.Prefix, row.PriorityLevel, row.IsPaid, row.FeeAmount, currency, row.IsActive, row.RowVersion, company))
            .ToList();
    }
}
