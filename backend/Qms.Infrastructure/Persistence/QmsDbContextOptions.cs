using Microsoft.EntityFrameworkCore;

namespace Qms.Infrastructure.Persistence;

public static class QmsDbContextOptions
{
    /// <summary>The one place that picks Npgsql, snake_case names and the migrations assembly.</summary>
    public static DbContextOptionsBuilder UseQmsPostgres(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(QmsDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
    }
}
