using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Application.Localisation;

namespace Qms.Infrastructure.Localisation;

/// <summary>
/// <see cref="ICompanyLanguages"/> with one query per request (AC-26): the registry joined to the caller's company row.
/// The tenant filter leaves at most one company, so the join adds no rows; without a company (anonymous caller, Super Admin)
/// only the registry is read. The result is kept for the rest of the request.
/// </summary>
public sealed class CompanyLanguages(IQmsDbContext db, ICurrentTenant tenant) : ICompanyLanguages
{
    private Task<LanguageSnapshot>? _snapshot;

    public Task<LanguageSnapshot> GetAsync(CancellationToken cancellationToken) => _snapshot ??= LoadAsync(cancellationToken);

    private async Task<LanguageSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (tenant.CompanyId is null)
        {
            var registry = await db.Languages.AsNoTracking()
                .OrderBy(language => language.SortOrder).ThenBy(language => language.Code)
                .Select(language => new LanguageInfo(language.Code, language.NativeName, language.Direction, language.IsAvailable, language.SortOrder))
                .ToListAsync(cancellationToken);
            return new LanguageSnapshot(registry, null);
        }

        var rows = await (
                from language in db.Languages.AsNoTracking()
                from company in db.Companies.AsNoTracking().DefaultIfEmpty()
                orderby language.SortOrder, language.Code
                select new
                {
                    language.Code,
                    language.NativeName,
                    language.Direction,
                    language.IsAvailable,
                    language.SortOrder,
                    DefaultLanguage = company == null ? null : company.DefaultLanguage,
                    EnabledLanguages = company == null ? null : company.EnabledLanguages,
                })
            .ToListAsync(cancellationToken);

        var languages = rows.Select(row => new LanguageInfo(row.Code, row.NativeName, row.Direction, row.IsAvailable, row.SortOrder)).ToList();
        var first = rows.FirstOrDefault();
        var settings = first?.DefaultLanguage is { } defaultLanguage && first.EnabledLanguages is { } enabled
            ? new CompanyLanguageSettings(defaultLanguage.Trim(), [.. enabled.InRegistryOrder(languages)])
            : null;
        return new LanguageSnapshot(languages, settings);
    }
}
