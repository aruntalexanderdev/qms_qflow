using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Common;

namespace Qms.Application.PriorityTypes;

/// <summary>Replaces every editable field. Deactivate or reactivate with <c>IsActive</c>; change the fee with <c>FeeAmount</c>.</summary>
[Audited("priority_type", "update")]
public sealed record UpdatePriorityTypeCommand(
    Guid Id, string? NameEn, string? NameAr, string? Prefix, int PriorityLevel, bool IsPaid, decimal? FeeAmount, bool IsActive, string? Version)
    : IPriorityTypeFields;

public sealed class UpdatePriorityTypeValidator : PriorityTypeFieldsValidator<UpdatePriorityTypeCommand>
{
    public UpdatePriorityTypeValidator()
    {
        RuleFor(command => command.Version).RowVersionText();
    }
}

public sealed class UpdatePriorityTypeHandler(
    IQmsDbContext db, IPostCommitActions postCommit, IDeviceConfigNotifier notifier)
    : ICommandHandler<UpdatePriorityTypeCommand, PriorityTypeDto>
{
    public async Task<Result<PriorityTypeDto>> HandleAsync(UpdatePriorityTypeCommand command, CancellationToken cancellationToken)
    {
        var row = await db.PriorityTypes.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);
        if (row is null)
        {
            return PriorityTypeErrors.NotFound;
        }

        var version = CommonRules.DecodeVersion(command.Version);
        if (row.RowVersion != version)
        {
            return CommonErrors.Conflict;
        }

        var prefix = command.Prefix!.Trim();
        if (prefix != row.Prefix && await db.PriorityTypes.AnyAsync(other => other.Prefix == prefix && other.Id != row.Id, cancellationToken))
        {
            return PriorityTypeErrors.PrefixTaken;
        }

        db.Entry(row).Property(r => r.RowVersion).OriginalValue = version;

        var nameEn = command.NameEn!.Trim();
        var nameAr = command.NameAr!.Trim();
        var fee = command.IsPaid ? command.FeeAmount : null;
        var devicesAffected = row.NameEn != nameEn || row.NameAr != nameAr || row.Prefix != prefix || row.IsActive != command.IsActive
            || row.IsPaid != command.IsPaid || row.FeeAmount != fee;
        row.NameEn = nameEn;
        row.NameAr = nameAr;
        row.Prefix = prefix;
        row.PriorityLevel = command.PriorityLevel;
        row.IsPaid = command.IsPaid;
        row.FeeAmount = fee;
        row.IsActive = command.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        if (devicesAffected)
        {
            var change = new DeviceConfigChange(row.CompanyId);
            postCommit.Enqueue(token => notifier.ConfigChangedAsync(change, token));
        }

        return PriorityTypeDto.From(row, await PriorityTypeDto.CurrencyOfCompanyAsync(db, cancellationToken));
    }
}
