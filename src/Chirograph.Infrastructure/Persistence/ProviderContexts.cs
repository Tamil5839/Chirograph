using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chirograph.Infrastructure.Persistence;

/// <summary>SQLite, for local development and tests.</summary>
public sealed class SqliteChirographDbContext(DbContextOptions<SqliteChirographDbContext> options)
    : ChirographDbContext(options)
{
    private const int SqliteConstraintError = 19;
    private const int SqliteConstraintUnique = 2067;

    public override bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError, SqliteExtendedErrorCode: SqliteConstraintUnique };

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite has no timestamp type and EF Core cannot order or compare DateTimeOffset there. All Chirograph
        // timestamps are UTC, so store them as fixed-width ISO 8601 text, which sorts chronologically.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<ValueConverters.UtcIsoTimestampConverter>();
    }
}

/// <summary>PostgreSQL, for production. Timestamps map to <c>timestamp with time zone</c>.</summary>
public sealed class PostgresChirographDbContext(DbContextOptions<PostgresChirographDbContext> options)
    : ChirographDbContext(options)
{
    public override bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
