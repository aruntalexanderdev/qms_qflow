using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.Localisation.CompanyLanguages;

/// <summary>
/// Replaces the enabled languages of the current company (full replacement, ADR-008 Amendment 3 A). The company comes from
/// the token; the default language is never changed by this command (ADR-010 section 1).
/// </summary>
[Audited("company_languages", "update")]
public sealed record UpdateCompanyLanguagesCommand(IReadOnlyList<string?>? EnabledLanguages, string? Version);

public sealed class UpdateCompanyLanguagesValidator : AbstractValidator<UpdateCompanyLanguagesCommand>
{
    public UpdateCompanyLanguagesValidator()
    {
        RuleFor(command => command.EnabledLanguages).Sent();
        RuleFor(command => command.EnabledLanguages)
            .Must(codes => codes!.All(code => !string.IsNullOrWhiteSpace(code)))
            .WithMessage("'{PropertyName}' must not contain an empty language code.")
            .When(command => command.EnabledLanguages is not null);
        RuleFor(command => command.EnabledLanguages)
            .Must(codes => codes!.Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == codes!.Count(code => !string.IsNullOrWhiteSpace(code)))
            .WithMessage("'{PropertyName}' must not list a language more than once.")
            .When(command => command.EnabledLanguages is not null);
        RuleFor(command => command.Version).RowVersionText();
    }
}

public sealed class UpdateCompanyLanguagesHandler(
    IQmsDbContext db, ICompanyLanguages languages, IPostCommitActions postCommit, IDeviceConfigNotifier notifier)
    : ICommandHandler<UpdateCompanyLanguagesCommand, CompanyLanguagesDto>
{
    public async Task<Result<CompanyLanguagesDto>> HandleAsync(UpdateCompanyLanguagesCommand command, CancellationToken cancellationToken)
    {
        var company = await db.Companies.FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return CommonErrors.ResourceNotFound;
        }

        // A stale version is a conflict even when the new values equal the stored ones (ADR-008 section 5).
        var version = CommonRules.DecodeVersion(command.Version);
        if (company.RowVersion != version)
        {
            return CommonErrors.Conflict;
        }

        var registry = (await languages.GetAsync(cancellationToken)).Registry;
        var submitted = command.EnabledLanguages!.Select(code => code!.Trim()).ToList();
        var unavailable = submitted
            .Where(code => !registry.Any(language => language.IsAvailable && string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unavailable.Count > 0)
        {
            return CompanyLanguageErrors.NotAvailable(unavailable);
        }

        // Registry form and registry order; the company default must stay on the list.
        var wanted = submitted
            .Select(code => registry.First(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)).Code)
            .InRegistryOrder(registry);
        if (!wanted.Contains(company.DefaultLanguage, StringComparer.OrdinalIgnoreCase))
        {
            return CompanyLanguageErrors.DefaultRequired(company.DefaultLanguage);
        }

        var removed = company.EnabledLanguages
            .Where(code => !wanted.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (removed.Count > 0)
        {
            var devices = await DispensersListingAsync(removed, cancellationToken);
            if (devices.Count > 0)
            {
                return CompanyLanguageErrors.InUse(removed, devices);
            }
        }

        // The UPDATE carries "AND xmin = version": a save racing between the load and here fails with a concurrency error (409).
        db.Entry(company).Property(row => row.RowVersion).OriginalValue = version;

        var changed = !company.EnabledLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(wanted);
        if (changed)
        {
            company.EnabledLanguages = [.. wanted];
        }

        await db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            // Every branch of the company: the dispensers and displays there offer other languages now (no-op until STORY-021).
            var companyId = company.Id;
            var branchIds = await db.Branches.AsNoTracking().OrderBy(branch => branch.Id).Select(branch => branch.Id).ToListAsync(cancellationToken);
            foreach (var branchId in branchIds)
            {
                var change = new DeviceConfigChange(companyId, branchId);
                postCommit.Enqueue(token => notifier.ConfigChangedAsync(change, token));
            }
        }

        return CompanyLanguagesDto.From(company, registry);
    }

    private async Task<List<DispenserUsingLanguage>> DispensersListingAsync(IReadOnlyList<string> removed, CancellationToken cancellationToken)
    {
        var devices = new Dictionary<Guid, DispenserUsingLanguage>();
        foreach (var code in removed)
        {
            var found = await db.DispenserSettings.AsNoTracking()
                .Where(setting => setting.Languages.Contains(code))
                .Select(setting => new DispenserUsingLanguage(setting.DeviceId, setting.Device!.Name))
                .ToListAsync(cancellationToken);
            foreach (var device in found)
            {
                devices[device.Id] = device;
            }
        }

        return [.. devices.Values.OrderBy(device => device.Name).ThenBy(device => device.Id)];
    }
}
