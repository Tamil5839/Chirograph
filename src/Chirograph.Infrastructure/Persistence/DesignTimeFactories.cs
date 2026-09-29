using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chirograph.Infrastructure.Persistence;

// Used only by `dotnet ef` to build the model when adding migrations; no database connection is made.

public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteChirographDbContext>
{
    public SqliteChirographDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteChirographDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresChirographDbContext>
{
    public PostgresChirographDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresChirographDbContext>().UseNpgsql("Host=localhost;Database=chirograph").Options);
}
