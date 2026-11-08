using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Domain.Entities;

namespace Qms.Application.ServiceTypes;

/// <summary>Creates a service type. The company comes from the token, never from the body (ADR-008 section 3).</summary>
[Audited("service_type", "create")]
public sealed record CreateServiceTypeCommand(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int? TargetMinutes, int SortOrder, bool IsActive);

public sealed class CreateServiceTypeValidator : AbstractValidator<CreateServiceTypeCommand>
{
    public CreateServiceTypeValidator()
    {
        RuleFor(command => command.Name).LocalizedName();
        RuleFor(command => command.Translations).LocalizedTranslations();
        RuleFor(command => command.Prefix).PrefixLetter();
        RuleFor(command => command.TargetMinutes).GreaterThan(0).When(command => command.TargetMinutes is not null);
        RuleFor(command => command.SortOrder).SortOrder();
    }
}

public sealed class CreateServiceTypeHandler(IQmsDbContext db, ICurrentTenant tenant) : ICommandHandler<CreateServiceTypeCommand, ServiceTypeDto>
{
    public async Task<Result<ServiceTypeDto>> HandleAsync(CreateServiceTypeCommand command, CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("A service type was created with no current company.");
        var prefix = command.Prefix!.Trim();

        // Friendly answer first; the unique index (company_id, prefix) stays the backstop for a race (mapped to the same code).
        if (await db.ServiceTypes.AnyAsync(row => row.Prefix == prefix, cancellationToken))
        {
            return ServiceTypeErrors.PrefixTaken;
        }

        var (nameEn, nameAr) = LocalizedText.ToStorage(command.Name!, command.Translations);
        var row = new ServiceType
        {
            CompanyId = companyId,
            NameEn = nameEn,
            NameAr = nameAr,
            Prefix = prefix,
            TargetMinutes = command.TargetMinutes,
            SortOrder = command.SortOrder,
            IsActive = command.IsActive,
        };
        db.ServiceTypes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceTypeDto.From(row);
    }
}
