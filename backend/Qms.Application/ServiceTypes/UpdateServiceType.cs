using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;
using Qms.Application.Localisation;

namespace Qms.Application.ServiceTypes;

/// <summary>Replaces every editable field (no partial update). Deactivate or reactivate with <c>IsActive</c>.</summary>
[Audited("service_type", "update")]
public sealed record UpdateServiceTypeCommand(
    Guid Id, string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int? TargetMinutes, int? SortOrder, bool? IsActive, string? Version);

public sealed class UpdateServiceTypeValidator : AbstractValidator<UpdateServiceTypeCommand>
{
    public UpdateServiceTypeValidator(ICompanyLanguages languages)
    {
        RuleFor(command => command.Name).DefaultValue();
        RuleFor(command => command.Translations).Translations(languages);
        RuleFor(command => command.Prefix).PrefixLetter();
        RuleFor(command => command.TargetMinutes).GreaterThan(0).When(command => command.TargetMinutes is not null);
        RuleFor(command => command.SortOrder).Sent();
        RuleFor(command => command.SortOrder).GreaterThanOrEqualTo(0).When(command => command.SortOrder is not null).WithMessage("'{PropertyName}' must be 0 or more.");
        RuleFor(command => command.IsActive).Sent();
        RuleFor(command => command.Version).RowVersionText();
    }
}

public sealed class UpdateServiceTypeHandler(
    IQmsDbContext db, ICompanyLanguages languages, IPostCommitActions postCommit, IDeviceConfigNotifier notifier)
    : ICommandHandler<UpdateServiceTypeCommand, ServiceTypeDto>
{
    public async Task<Result<ServiceTypeDto>> HandleAsync(UpdateServiceTypeCommand command, CancellationToken cancellationToken)
    {
        var row = await db.ServiceTypes.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (row is null)
        {
            return ServiceTypeErrors.NotFound;
        }

        // A stale version is a conflict even when the new values equal the stored ones (no UPDATE would be sent then).
        var version = CommonRules.DecodeVersion(command.Version);
        if (row.RowVersion != version)
        {
            return CommonErrors.Conflict;
        }

        var prefix = command.Prefix!.Trim();
        if (prefix != row.Prefix && await db.ServiceTypes.AnyAsync(other => other.Prefix == prefix && other.Id != row.Id, cancellationToken))
        {
            return ServiceTypeErrors.PrefixTaken;
        }

        // The UPDATE then carries "AND xmin = version": a save racing between the load and here fails with a concurrency error (409).
        db.Entry(row).Property(r => r.RowVersion).OriginalValue = version;

        // Translations of languages that are not enabled stay as they are (BR-9); the enabled ones are replaced.
        var company = await languages.GetCompanySettingsAsync(cancellationToken);
        var name = command.Name!.Trim();
        var translations = LocalizedTextRules.Merge(row.NameTranslations, command.Translations, company);
        var devicesAffected = row.Name != name || !LocalizedTextRules.Same(row.NameTranslations, translations) || row.Prefix != prefix || row.IsActive != command.IsActive!.Value;
        row.Name = name;
        row.NameTranslations = translations;
        row.Prefix = prefix;
        row.TargetMinutes = command.TargetMinutes;
        row.SortOrder = command.SortOrder!.Value;
        row.IsActive = command.IsActive!.Value;
        await db.SaveChangesAsync(cancellationToken);

        if (devicesAffected)
        {
            var change = new DeviceConfigChange(row.CompanyId);
            postCommit.Enqueue(token => notifier.ConfigChangedAsync(change, token));
        }

        return ServiceTypeDto.From(row, company);
    }
}
