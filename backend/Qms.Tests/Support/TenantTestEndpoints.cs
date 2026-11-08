using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Qms.Api.Common;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;

namespace Qms.Tests.Support;

// Test-host-only endpoints and handlers for STORY-003 AC-9..AC-22. STORY-003 adds no production endpoint that writes
// tenant data, so the tenant guard, [CrossTenant] and the exception mapping are proven here. They are registered only
// by QmsApiFactory (assembly scanning of the test assembly); nothing in Qms.Api refers to them.

/// <summary>Records what a handler saw, so a test can prove a body did or did not run.</summary>
public sealed class HandlerSpy
{
    public int Runs { get; set; }

    public bool? SawCrossTenant { get; set; }
}

public sealed record BranchRow(Guid Id, string Code, Guid CompanyId);

public sealed record ListBranchesQuery;

/// <summary>Tenant-filtered read: only the current company's rows (AC-21).</summary>
public sealed class ListBranchesHandler(QmsDbContext db) : IQueryHandler<ListBranchesQuery, IReadOnlyList<BranchRow>>
{
    public async Task<Result<IReadOnlyList<BranchRow>>> HandleAsync(ListBranchesQuery query, CancellationToken cancellationToken) =>
        await db.Branches.OrderBy(b => b.Code).Select(b => new BranchRow(b.Id, b.Code, b.CompanyId)).ToListAsync(cancellationToken);
}

public sealed record ListAllBranchesQuery;

/// <summary>Cross-tenant read: every company's rows through IgnoreQueryFilters (AC-22).</summary>
[CrossTenant]
public sealed class ListAllBranchesHandler(QmsDbContext db, HandlerSpy spy, ICurrentTenant tenant)
    : IQueryHandler<ListAllBranchesQuery, IReadOnlyList<BranchRow>>
{
    public async Task<Result<IReadOnlyList<BranchRow>>> HandleAsync(ListAllBranchesQuery query, CancellationToken cancellationToken)
    {
        spy.Runs++;
        spy.SawCrossTenant = tenant.IsCrossTenant;
        return await db.Branches.IgnoreQueryFilters().OrderBy(b => b.Code).Select(b => new BranchRow(b.Id, b.Code, b.CompanyId)).ToListAsync(cancellationToken);
    }
}

public sealed record CreateBranchForCompanyCommand(Guid CompanyId, string Code);

/// <summary>Cross-tenant write with an explicit CompanyId (AC-11, AC-12).</summary>
[CrossTenant]
public sealed class CreateBranchForCompanyHandler(QmsDbContext db, HandlerSpy spy, ICurrentTenant tenant)
    : ICommandHandler<CreateBranchForCompanyCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateBranchForCompanyCommand command, CancellationToken cancellationToken)
    {
        spy.Runs++;
        spy.SawCrossTenant = tenant.IsCrossTenant;
        var branch = new Branch
        {
            CompanyId = command.CompanyId, Name = "Created", Code = command.Code, Timezone = "Asia/Dubai",
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);
        return branch.Id;
    }
}

public sealed record CheckUserQuery(Guid UserId);

/// <summary>A user id from the request must belong to the current company, else user.not_found (AC-17).</summary>
public sealed class CheckUserHandler(IUserDirectory directory, ICurrentUser currentUser) : IQueryHandler<CheckUserQuery, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CheckUserQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.CompanyId is not { } companyId || !await directory.IsInCompanyAsync(query.UserId, companyId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        return query.UserId;
    }
}

[Route(ApiRoutes.Prefix + "/test/tenant")]
public sealed class TenantTestController(QmsDbContext db) : QmsControllerBase
{
    [HttpGet("branches")]
    public async Task<IActionResult> Branches(
        [FromServices] IQueryHandler<ListBranchesQuery, IReadOnlyList<BranchRow>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListBranchesQuery(), cancellationToken)).ToActionResult(this);

    [HttpGet("all-branches")]
    public async Task<IActionResult> AllBranches(
        [FromServices] IQueryHandler<ListAllBranchesQuery, IReadOnlyList<BranchRow>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListAllBranchesQuery(), cancellationToken)).ToActionResult(this);

    [HttpPost("branches")]
    public async Task<IActionResult> CreateBranch(
        CreateBranchForCompanyCommand command,
        [FromServices] ICommandHandler<CreateBranchForCompanyCommand, Guid> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(command, cancellationToken)).ToActionResult(this);

    /// <summary>Runs the [CrossTenant] handler, then a plain tenant write in the same scope: that one must fail (AC-12).</summary>
    [HttpPost("branches-then-tenant-write")]
    public async Task<IActionResult> CreateBranchThenWrite(
        CreateBranchForCompanyCommand command,
        [FromServices] ICommandHandler<CreateBranchForCompanyCommand, Guid> handler,
        [FromServices] ICurrentTenant tenant,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(command, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!.ToProblem(this);
        }

        var sectionOpenAfterHandler = tenant.IsCrossTenant;
        db.Branches.Add(new Branch { CompanyId = command.CompanyId, Name = "Late", Code = command.Code + "L", Timezone = "Asia/Dubai" });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new { sectionOpenAfterHandler, followUp = "saved" });
        }
        catch (TenantViolationException exception)
        {
            return Ok(new { sectionOpenAfterHandler, followUp = exception.Reason.ToString() });
        }
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> CheckUser(
        Guid userId, [FromServices] IQueryHandler<CheckUserQuery, Guid> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new CheckUserQuery(userId), cancellationToken)).ToActionResult(this);

    /// <summary>
    /// AC-18. Loads the counter, lets "another request" delete the row first (raw SQL, as a second connection would),
    /// then saves a change: PostgreSQL matches 0 rows and EF raises DbUpdateConcurrencyException.
    /// </summary>
    [HttpPut("counters/{id:guid}/racing-rename")]
    public async Task<IActionResult> RacingRename(Guid id, CancellationToken cancellationToken)
    {
        var counter = await db.Counters.SingleAsync(c => c.Id == id, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM counter WHERE id = {id}", cancellationToken);
        counter.Name = "Renamed";
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>AC-19 / AC-20. The mode picks the violation; the body carries the foreign company id.</summary>
    [HttpPost("touch")]
    public async Task<IActionResult> Touch(TouchRequest request, CancellationToken cancellationToken)
    {
        switch (request.Mode)
        {
            case "update-other-company":
                // A forged detached row of another company: the guard refuses it before SQL is sent.
                db.Branches.Update(new Branch
                {
                    Id = request.BranchId, CompanyId = request.OtherCompanyId, Name = "x", Code = "X", Timezone = "UTC",
                });
                break;
            case "change-company":
                var own = await db.Branches.SingleAsync(b => b.Id == request.BranchId, cancellationToken);
                own.CompanyId = request.OtherCompanyId;
                break;
            case "no-tenant":
                db.Branches.Add(new Branch
                {
                    CompanyId = request.OtherCompanyId, Name = "x", Code = "NT", Timezone = "UTC",
                });
                break;
            default:
                return BadRequest();
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record TouchRequest(string Mode, Guid BranchId, Guid OtherCompanyId);

/// <summary>An endpoint with no attribute at all: protected by the fallback policy (AC-6).</summary>
[Route(ApiRoutes.Prefix + "/test/protected")]
public sealed class ProtectedByDefaultController : QmsControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { ok = true });

    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok(new { ok = true });
}

/// <summary>Stands in for a SignalR hub path: the access_token query parameter is accepted only under /hubs.</summary>
[Route("hubs/test")]
public sealed class FakeHubController(ICurrentUser currentUser) : QmsControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { userId = currentUser.UserId });
}
