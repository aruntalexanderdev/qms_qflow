using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Domain.Entities;

namespace Qms.Application.PriorityTypes;

/// <summary>Creates a priority type. The company comes from the token; the currency is the company's (read only).</summary>
[Audited("priority_type", "create")]
public sealed record CreatePriorityTypeCommand(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int? PriorityLevel, bool? IsPaid, decimal? FeeAmount, bool IsActive)
    : IPriorityTypeFields;

public sealed class CreatePriorityTypeValidator(ICompanyLanguages languages) : PriorityTypeFieldsValidator<CreatePriorityTypeCommand>(languages);

public sealed class CreatePriorityTypeHandler(IQmsDbContext db, ICurrentTenant tenant, ICompanyLanguages languages) : ICommandHandler<CreatePriorityTypeCommand, PriorityTypeDto>
{
    public async Task<Result<PriorityTypeDto>> HandleAsync(CreatePriorityTypeCommand command, CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("A priority type was created with no current company.");
        var prefix = command.Prefix!.Trim();

        if (await db.PriorityTypes.AnyAsync(row => row.Prefix == prefix, cancellationToken))
        {
            return PriorityTypeErrors.PrefixTaken;
        }

        var company = await languages.GetCompanySettingsAsync(cancellationToken);
        var row = new PriorityType
        {
            CompanyId = companyId,
            Name = command.Name!.Trim(),
            NameTranslations = LocalizedTextRules.Merge(null, command.Translations, company),
            Prefix = prefix,
            PriorityLevel = command.PriorityLevel!.Value,
            IsPaid = command.IsPaid!.Value,
            FeeAmount = command.IsPaid.Value ? command.FeeAmount : null,
            IsActive = command.IsActive,
        };
        db.PriorityTypes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return PriorityTypeDto.From(row, await PriorityTypeDto.CurrencyOfCompanyAsync(db, cancellationToken), company);
    }
}
