namespace Qms.Application.Common;

/// <summary>
/// Which branches of the current company the signed-in user may work in (ADR-003 decision 4, ADR-008 section 10).
/// A Company Admin passes every branch of the company; a Branch Manager (or Counter Staff) only the branches in
/// <c>user_branch</c>. The assignments are read once per request. Implemented in Qms.Infrastructure.
/// Order in a handler: load the target through the tenant-filtered set (not found or another company = 404), then
/// <see cref="EnsureAccessAsync"/> (another branch = 403).
/// </summary>
public interface IBranchScope
{
    /// <summary>
    /// Success when the user may use <paramref name="branchId"/>. An unknown branch or one of another company is
    /// <c>resource.not_found</c> (404, the same answer for both); a branch of this company the user is not assigned to
    /// is <c>auth.forbidden</c> (403). A user without a company (Super Admin) is always forbidden.
    /// </summary>
    Task<Result> EnsureAccessAsync(Guid branchId, CancellationToken cancellationToken);

    /// <summary>The branch ids the user may use; <c>null</c> means every branch of the company (Company Admin).</summary>
    Task<IReadOnlyCollection<Guid>?> AccessibleBranchIdsAsync(CancellationToken cancellationToken);
}
