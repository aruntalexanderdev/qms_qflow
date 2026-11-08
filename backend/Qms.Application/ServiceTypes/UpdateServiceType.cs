using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.ServiceTypes;

/// <summary>Replaces every editable field (no partial update). Deactivate or reactivate with <c>IsActive</c>.</summary>
[Audited("service_type", "update")]
public sealed record UpdateServiceTypeCommand(
    Guid Id, string? NameEn, string? NameAr, string? Prefix, int? TargetMinutes, int SortOrder, bool IsActive, string? Version);

public sealed class UpdateServiceTypeValidator : AbstractValidator<UpdateServiceTypeCommand>
{
    public UpdateServiceTypeValidator()
    {
        RuleFor(command => command.NameEn).BilingualName();
        RuleFor(command => command.NameAr).BilingualName();
        RuleFor(command => command.Prefix).PrefixLetter();
        RuleFor(command => command.TargetMinutes).GreaterThan(0).When(command => command.TargetMinutes is not null);
        RuleFor(command => command.SortOrder).SortOrder();
        RuleFor(command => command.Version).RowVersionText();
    }
}

public sealed class UpdateServiceTypeHandler(
    IQmsDbContext db, IPostCommitActions postCommit, IDeviceConfigNotifier notifier)
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

        var nameEn = command.NameEn!.Trim();
        var nameAr = command.NameAr!.Trim();
        var devicesAffected = row.NameEn != nameEn || row.NameAr != nameAr || row.Prefix != prefix || row.IsActive != command.IsActive;
        row.NameEn = nameEn;
        row.NameAr = nameAr;
        row.Prefix = prefix;
        row.TargetMinutes = command.TargetMinutes;
        row.SortOrder = command.SortOrder;
        row.IsActive = command.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        if (devicesAffected)
        {
            var change = new DeviceConfigChange(row.CompanyId);
            postCommit.Enqueue(token => notifier.ConfigChangedAsync(change, token));
        }

        return ServiceTypeDto.From(row);
    }
}
