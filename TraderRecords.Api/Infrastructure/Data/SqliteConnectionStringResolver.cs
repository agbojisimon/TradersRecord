using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// The resolved SQLite location and the full connection string used by EF Core.
/// </summary>
public sealed record SqliteConnectionSettings(string DataSource, string ConnectionString);

/// <summary>
/// Resolves relative SQLite paths against the application content root while
/// preserving absolute paths and special SQLite data sources from configuration.
/// </summary>
public static class SqliteConnectionStringResolver
{
    public static SqliteConnectionSettings Resolve(
        IConfiguration configuration,
        string contentRootPath)
    {
        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");

        var connectionString = new SqliteConnectionStringBuilder(configuredConnectionString);
        var configuredDataSource = connectionString.DataSource;

        if (string.IsNullOrWhiteSpace(configuredDataSource))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' must specify a SQLite Data Source.");
        }

        if (!IsSpecialSqliteDataSource(configuredDataSource)
            && !Path.IsPathRooted(configuredDataSource))
        {
            connectionString.DataSource = Path.GetFullPath(
                Path.Combine(contentRootPath, configuredDataSource));
        }

        var resolvedDataSource = connectionString.DataSource;

        if (!IsSpecialSqliteDataSource(resolvedDataSource))
        {
            var databaseDirectory = Path.GetDirectoryName(resolvedDataSource);

            if (!string.IsNullOrEmpty(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }
        }

        return new SqliteConnectionSettings(
            resolvedDataSource,
            connectionString.ConnectionString);
    }

    private static bool IsSpecialSqliteDataSource(string dataSource) =>
        dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
        || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
}
