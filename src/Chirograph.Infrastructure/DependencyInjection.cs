using Chirograph.Application.Abstractions;
using Chirograph.Infrastructure.Dns;
using Chirograph.Infrastructure.Email;
using Chirograph.Infrastructure.Pdf;
using Chirograph.Infrastructure.Persistence;
using Chirograph.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chirograph.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply pending EF Core migrations when the app starts (convenient in development).</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

public static class DependencyInjection
{
    /// <summary>Name of the connection string: <c>ConnectionStrings:Chirograph</c> / <c>ConnectionStrings__Chirograph</c>.</summary>
    public const string ConnectionStringName = "Chirograph";

    private const string DefaultSqliteConnectionString = "Data Source=App_Data/chirograph.db";

    public static IServiceCollection AddChirographInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseSection = configuration.GetSection(DatabaseOptions.Section);
        services.Configure<DatabaseOptions>(databaseSection);
        var database = databaseSection.Get<DatabaseOptions>() ?? new DatabaseOptions();
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (database.Provider == DatabaseProvider.Postgres)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is required when Database:Provider is Postgres. " +
                    $"Set it with user-secrets or the ConnectionStrings__{ConnectionStringName} environment variable.");
            }
            services.AddDbContext<ChirographDbContext, PostgresChirographDbContext>(options => options.UseNpgsql(connectionString));
        }
        else
        {
            var sqlite = ResolveSqliteConnectionString(connectionString ?? DefaultSqliteConnectionString, contentRootPath);
            services.AddDbContext<ChirographDbContext, SqliteChirographDbContext>(options => options.UseSqlite(sqlite));
        }
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<ChirographDbContext>());

        services.AddOptions<FileStoreOptions>()
            .Bind(configuration.GetSection(FileStoreOptions.Section))
            .PostConfigure(options => options.RootPath = Absolute(options.RootPath, contentRootPath));
        services.AddSingleton<IFileStore, LocalDiskFileStore>();

        var emailSection = configuration.GetSection(EmailOptions.Section);
        services.AddOptions<EmailOptions>()
            .Bind(emailSection)
            .PostConfigure(options => options.OutboxDirectory = Absolute(options.OutboxDirectory, contentRootPath));
        if ((emailSection.Get<EmailOptions>() ?? new EmailOptions()).Delivery == EmailDelivery.Smtp)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, OutboxEmailSender>();
        services.AddSingleton<OutboxMailbox>();

        services.AddOptions<DnsOptions>().Bind(configuration.GetSection(DnsOptions.Section));
        services.AddSingleton<IDnsTxtResolver, DnsClientTxtResolver>();

        services.AddSingleton<IPdfStamper, PdfSharpStamper>();
        return services;
    }

    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ChirographDbContext>();
        await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Anchors a relative SQLite file path to the content root and makes sure its folder exists.</summary>
    private static string ResolveSqliteConnectionString(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || builder.Mode == SqliteOpenMode.Memory
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return builder.ToString();
        }

        builder.DataSource = Absolute(dataSource, contentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        return builder.ToString();
    }

    private static string Absolute(string path, string contentRootPath) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(contentRootPath, path));
}
