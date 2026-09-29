using Chirograph.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chirograph.UnitTests.Support;

internal interface ITestDatabase : IAsyncDisposable
{
    ChirographDbContext CreateContext();
}

/// <summary>An in-memory SQLite database with the real migrations applied, shared by the contexts it creates.</summary>
internal sealed class SqliteTestDatabase : ITestDatabase
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var database = new SqliteTestDatabase(connection);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }

    public ChirographDbContext CreateContext() =>
        new SqliteChirographDbContext(new DbContextOptionsBuilder<SqliteChirographDbContext>().UseSqlite(_connection).Options);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>A throw-away PostgreSQL database with the real migrations applied; dropped on dispose.</summary>
internal sealed class PostgresTestDatabase : ITestDatabase
{
    public const string Variable = "CHIROGRAPH_TEST_POSTGRES";

    private readonly string _connectionString;

    private PostgresTestDatabase(string connectionString) => _connectionString = connectionString;

    public static async Task<PostgresTestDatabase> CreateAsync(string serverConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = $"chirograph_test_{Guid.NewGuid():N}" };
        var database = new PostgresTestDatabase(builder.ConnectionString);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }

    public ChirographDbContext CreateContext() =>
        new PostgresChirographDbContext(new DbContextOptionsBuilder<PostgresChirographDbContext>().UseNpgsql(_connectionString).Options);

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
