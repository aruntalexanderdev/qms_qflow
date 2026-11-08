using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qms.Application.Common;

namespace Qms.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> (migrations add / bundle). Needs no running API or database to create a migration.
/// Set <c>QMS_DESIGN_CONNECTION</c> to point the tool at a real database (for example <c>dotnet ef database update</c>).
/// </summary>
public sealed class QmsDbContextFactory : IDesignTimeDbContextFactory<QmsDbContext>
{
    public QmsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("QMS_DESIGN_CONNECTION") ?? "Host=localhost;Database=qms_design";
        var builder = new DbContextOptionsBuilder<QmsDbContext>();
        builder.UseQmsPostgres(connection);
        return new QmsDbContext(builder.Options, new NoTenant());
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? CompanyId => null;

        public bool IsCrossTenant => false;
    }
}
