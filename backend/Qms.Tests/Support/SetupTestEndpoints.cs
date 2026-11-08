using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Qms.Api.Common;
using Qms.Application.Common;
using Qms.Domain.Enums;

namespace Qms.Tests.Support;

// Test-host-only endpoints for the M2 plumbing (STORY-005 AC-28 to AC-30): enums as strings, the database error catalog and
// IBranchScope. Registered only by QmsApiFactory; nothing in Qms.Api refers to them.

/// <summary>Remembers every call to <see cref="IDeviceConfigNotifier"/> (AC-33).</summary>
public sealed class RecordingDeviceConfigNotifier : IDeviceConfigNotifier
{
    private readonly ConcurrentQueue<DeviceConfigChange> _calls = new();

    public IReadOnlyList<DeviceConfigChange> Calls => [.. _calls];

    public void Clear() => _calls.Clear();

    public Task ConfigChangedAsync(DeviceConfigChange change, CancellationToken cancellationToken)
    {
        _calls.Enqueue(change);
        return Task.CompletedTask;
    }
}

/// <summary>Records the SQL text of every command the host sends, so a test can count queries (AC-30).</summary>
public sealed class SqlCommandLog : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyList<string> Commands => [.. _commands];

    public void Clear() => _commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

public sealed record EnumBody(TokenStatus Status);

public sealed record BranchScopeProbe(Guid BranchId);

public sealed record BranchScopeAnswer(IReadOnlyCollection<Guid>? Accessible);

/// <summary>Loads the branch through the tenant-filtered set first (404 for unknown or other company), then asks the scope (AC-30).</summary>
public sealed class BranchScopeProbeHandler(IQmsDbContext db, IBranchScope scope) : IQueryHandler<BranchScopeProbe, BranchScopeAnswer>
{
    public async Task<Result<BranchScopeAnswer>> HandleAsync(BranchScopeProbe query, CancellationToken cancellationToken)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == query.BranchId, cancellationToken))
        {
            return CommonErrors.ResourceNotFound;
        }

        var access = await scope.EnsureAccessAsync(query.BranchId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        // Asked several times on purpose: the user_branch rows are still read once.
        await scope.EnsureAccessAsync(query.BranchId, cancellationToken);
        return new BranchScopeAnswer(await scope.AccessibleBranchIdsAsync(cancellationToken));
    }
}

[Route(ApiRoutes.Prefix + "/test/setup")]
public sealed class SetupTestController : QmsControllerBase
{
    /// <summary>AC-28: returns an enum; the JSON must hold the name, not a number.</summary>
    [HttpGet("enum")]
    [AllowAnonymous]
    public IActionResult GetEnum() => Ok(new EnumBody(TokenStatus.Waiting));

    /// <summary>AC-28: accepts an enum by name and answers with it.</summary>
    [HttpPost("enum")]
    [AllowAnonymous]
    public IActionResult PostEnum(EnumBody body) => Ok(new { body.Status, isWaiting = body.Status == TokenStatus.Waiting });

    /// <summary>
    /// AC-29: raises the database error the query describes, with SQL text in the exception message that must never reach
    /// the client. <c>concurrency</c> raises the concurrency exception instead.
    /// </summary>
    [HttpPost("db-error")]
    [AllowAnonymous]
    public IActionResult DbError([FromQuery] string sqlState, [FromQuery] string? constraint)
    {
        if (sqlState == "concurrency")
        {
            throw new DbUpdateConcurrencyException("UPDATE secret_table SET x = 'value'");
        }

        var postgres = new PostgresException(
            "duplicate key value violates unique constraint \"" + constraint + "\" INSERT INTO secret_table (x) VALUES ('value')",
            "ERROR", "ERROR", sqlState, constraintName: constraint);
        throw new DbUpdateException("An error occurred while saving the entity changes.", postgres);
    }

    /// <summary>AC-11 / AC-29: deletes a service type WITHOUT the in-use pre-check, so the foreign key error reaches the exception handler.</summary>
    [HttpDelete("service-types/{id:guid}/unchecked")]
    public async Task<IActionResult> DeleteServiceTypeUnchecked(Guid id, [FromServices] IQmsDbContext db, CancellationToken cancellationToken)
    {
        db.ServiceTypes.Remove(await db.ServiceTypes.FirstAsync(s => s.Id == id, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("priority-types/{id:guid}/unchecked")]
    public async Task<IActionResult> DeletePriorityTypeUnchecked(Guid id, [FromServices] IQmsDbContext db, CancellationToken cancellationToken)
    {
        db.PriorityTypes.Remove(await db.PriorityTypes.FirstAsync(p => p.Id == id, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>AC-29: inserts a service type WITHOUT the prefix pre-check, so the unique index error reaches the exception handler.</summary>
    [HttpPost("service-types/unchecked")]
    public async Task<IActionResult> CreateServiceTypeUnchecked(
        [FromQuery] string prefix, [FromServices] IQmsDbContext db, [FromServices] ICurrentTenant tenant, CancellationToken cancellationToken)
    {
        db.ServiceTypes.Add(new Qms.Domain.Entities.ServiceType { CompanyId = tenant.CompanyId!.Value, NameEn = "x", NameAr = "x", Prefix = prefix });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>AC-29: a check constraint violation (target minutes 0) that no validator stopped.</summary>
    [HttpPost("service-types/unchecked-invalid")]
    public async Task<IActionResult> CreateInvalidServiceTypeUnchecked([FromServices] IQmsDbContext db, [FromServices] ICurrentTenant tenant, CancellationToken cancellationToken)
    {
        db.ServiceTypes.Add(new Qms.Domain.Entities.ServiceType { CompanyId = tenant.CompanyId!.Value, NameEn = "x", NameAr = "x", Prefix = "Y", TargetMinutes = 0 });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("branch-scope/{branchId:guid}")]
    public async Task<IActionResult> BranchScope(
        Guid branchId,
        [FromServices] IQueryHandler<BranchScopeProbe, BranchScopeAnswer> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new BranchScopeProbe(branchId), cancellationToken)).ToActionResult(this);
}
