using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.Localisation.CompanyLanguages;

public sealed record GetCompanyLanguagesQuery;

public sealed class GetCompanyLanguagesHandler(IQmsDbContext db, ICompanyLanguages languages)
    : IQueryHandler<GetCompanyLanguagesQuery, CompanyLanguagesDto>
{
    public async Task<Result<CompanyLanguagesDto>> HandleAsync(GetCompanyLanguagesQuery query, CancellationToken cancellationToken)
    {
        // The tenant filter leaves the caller's own company row (the endpoint has no company id).
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return CommonErrors.ResourceNotFound;
        }

        var snapshot = await languages.GetAsync(cancellationToken);
        return CompanyLanguagesDto.From(company, snapshot.Registry);
    }
}
