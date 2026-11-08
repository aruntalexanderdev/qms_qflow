using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qms.Application.Auth;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// Refresh tokens in the <c>refresh_token</c> table (ADR-004): SHA-256 hash only, one family per sign-in, rotated on
/// every use. A token that was already rotated or revoked revokes its whole family. The table has no company column, so
/// it is not tenant-filtered and needs no cross-tenant section.
/// </summary>
public sealed class RefreshTokenStore(QmsDbContext db, IOptions<JwtOptions> options, TimeProvider timeProvider) : IRefreshTokenStore
{
    private const int MaxIpLength = 64;

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, string? clientIp, CancellationToken cancellationToken)
    {
        var (row, plain) = NewRow(userId, Guid.CreateVersion7(), clientIp);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedRefreshToken(plain, row.ExpiresAt);
    }

    public async Task<RefreshRotation> RotateAsync(string token, string? clientIp, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hash = RefreshTokenHasher.Hash(token);
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (current is null || !RefreshTokenHasher.HashesEqual(current.TokenHash, hash))
        {
            return new RefreshRotation(RefreshRotationOutcome.Invalid);
        }

        if (current.RevokedAt is not null || current.ReplacedById is not null)
        {
            await RevokeFamilyRowsAsync(current.FamilyId, cancellationToken);
            return new RefreshRotation(RefreshRotationOutcome.Reused);
        }

        if (current.ExpiresAt <= now)
        {
            return new RefreshRotation(RefreshRotationOutcome.Invalid);
        }

        var (next, plain) = NewRow(current.UserId, current.FamilyId, clientIp);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        // Atomic: of two concurrent refreshes with the same token only one update matches. The loser is a reuse.
        var replaced = await db.RefreshTokens
            .Where(r => r.Id == current.Id && r.RevokedAt == null && r.ReplacedById == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(r => r.RevokedAt, now).SetProperty(r => r.ReplacedById, next.Id).SetProperty(r => r.UpdatedAt, now),
                cancellationToken);
        if (replaced == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeFamilyRowsAsync(current.FamilyId, cancellationToken);
            return new RefreshRotation(RefreshRotationOutcome.Reused);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RefreshRotation(RefreshRotationOutcome.Rotated, current.UserId, new IssuedRefreshToken(plain, next.ExpiresAt));
    }

    public async Task RevokeFamilyAsync(string token, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenHasher.Hash(token);
        var familyId = await db.RefreshTokens.AsNoTracking()
            .Where(r => r.TokenHash == hash)
            .Select(r => (Guid?)r.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } family)
        {
            await RevokeFamilyRowsAsync(family, cancellationToken);
        }
    }

    private async Task RevokeFamilyRowsAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, now).SetProperty(r => r.UpdatedAt, now), cancellationToken);
    }

    private (RefreshToken Row, string Plain) NewRow(Guid userId, Guid familyId, string? clientIp)
    {
        var plain = RefreshTokenHasher.NewToken();
        var row = new RefreshToken
        {
            UserId = userId,
            TokenHash = RefreshTokenHasher.Hash(plain),
            FamilyId = familyId,
            ExpiresAt = timeProvider.GetUtcNow().AddDays(options.Value.RefreshTokenDays),
            CreatedIp = clientIp is { Length: > MaxIpLength } ? clientIp[..MaxIpLength] : clientIp,
        };
        return (row, plain);
    }
}
