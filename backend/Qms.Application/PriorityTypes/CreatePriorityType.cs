using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Domain.Entities;

namespace Qms.Application.PriorityTypes;

/// <summary>Creates a priority type. The company comes from the token; the currency is the company's (read only).</summary>
[Audited("priority_type", "create")]
public sealed record CreatePriorityTypeCommand(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int PriorityLevel, bool IsPaid, decimal? FeeAmount, bool IsActive)
    : IPriorityTypeFields;

public sealed class CreatePriorityTypeValidator : PriorityTypeFieldsValidator<CreatePriorityTypeCommand>;

public sealed class CreatePriorityTypeHandler(IQmsDbContext db, ICurrentTenant tenant) : ICommandHandler<CreatePriorityTypeCommand, PriorityTypeDto>
{
    public async Task<Result<PriorityTypeDto>> HandleAsync(CreatePriorityTypeCommand command, CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("A priority type was created with no current company.");
        var prefix = command.Prefix!.Trim();

        if (await db.PriorityTypes.AnyAsync(row => row.Prefix == prefix, cancellationToken))
        {
            return PriorityTypeErrors.PrefixTaken;
        }

        var (nameEn, nameAr) = LocalizedText.ToStorage(command.Name!, command.Translations);
        var row = new PriorityType
        {
            CompanyId = companyId,
            NameEn = nameEn,
            NameAr = nameAr,
            Prefix = prefix,
            PriorityLevel = command.PriorityLevel,
            IsPaid = command.IsPaid,
            FeeAmount = command.IsPaid ? command.FeeAmount : null,
            IsActive = command.IsActive,
        };
        db.PriorityTypes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return PriorityTypeDto.From(row, await PriorityTypeDto.CurrencyOfCompanyAsync(db, cancellationToken));
    }
}
