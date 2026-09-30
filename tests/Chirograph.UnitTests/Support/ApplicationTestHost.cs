using System.Text.RegularExpressions;
using Chirograph.Application;
using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Documents;
using Chirograph.Infrastructure.Pdf;
using Chirograph.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Chirograph.UnitTests.Support;

/// <summary>
/// Hosts the application layer over a migrated in-memory SQLite database, the real PDF stamper, a controllable clock
/// and in-memory fakes for email, DNS and file storage. Each call runs in its own DI scope, like a web request.
/// </summary>
internal sealed partial class ApplicationTestHost : IAsyncDisposable
{
    public const string PublicBaseUrl = "https://verify.chirograph.test";

    private readonly SqliteConnection? _connection;
    private readonly ServiceProvider _services;
    private readonly bool _dropDatabaseOnDispose;

    private ApplicationTestHost(SqliteConnection? connection, ServiceProvider services, bool dropDatabaseOnDispose)
    {
        _connection = connection;
        _services = services;
        _dropDatabaseOnDispose = dropDatabaseOnDispose;
    }

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public CapturingEmailSender Email { get; } = new();

    public FakeDnsTxtResolver Dns { get; } = new();

    public InMemoryFileStore Files { get; } = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <param name="postgresServer">
    /// Optional PostgreSQL server connection string; a throw-away database is created on it instead of in-memory SQLite.
    /// </param>
    public static async Task<ApplicationTestHost> CreateAsync(Action<ChirographOptions>? configure = null, string? postgresServer = null)
    {
        SqliteConnection? connection = null;
        var services = new ServiceCollection();
        if (postgresServer is null)
        {
            connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Token);
            services.AddDbContext<ChirographDbContext, SqliteChirographDbContext>(options => options.UseSqlite(connection));
        }
        else
        {
            var database = new Npgsql.NpgsqlConnectionStringBuilder(postgresServer) { Database = $"chirograph_test_{Guid.NewGuid():N}" }.ConnectionString;
            services.AddDbContext<ChirographDbContext, PostgresChirographDbContext>(options => options.UseNpgsql(database));
        }

        ApplicationTestHost? host = null;
        services.AddLogging();
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<ChirographDbContext>());
        services.AddSingleton<TimeProvider>(_ => host!.Time);
        services.AddSingleton<IEmailSender>(_ => host!.Email);
        services.AddSingleton<IDnsTxtResolver>(_ => host!.Dns);
        services.AddSingleton<IFileStore>(_ => host!.Files);
        services.AddSingleton<IPdfStamper, PdfSharpStamper>();
        services.Configure<ChirographOptions>(options =>
        {
            options.PublicBaseUrl = new Uri(PublicBaseUrl);
            configure?.Invoke(options);
        });
        services.AddChirographApplication();

        host = new ApplicationTestHost(connection, services.BuildServiceProvider(validateScopes: true), dropDatabaseOnDispose: postgresServer is not null);
        await host.WithDatabaseAsync(db => ((ChirographDbContext)db).Database.MigrateAsync(Token));
        return host;
    }

    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task RunAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<TResult> WithDatabaseAsync<TResult>(Func<IAppDbContext, Task<TResult>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }

    public Task WithDatabaseAsync(Func<IAppDbContext, Task> action) =>
        WithDatabaseAsync(async db =>
        {
            await action(db);
            return true;
        });

    /// <summary>The magic-link token in an email body (the first one if there are several).</summary>
    public static string LinkToken(EmailMessage message)
    {
        var match = MagicLinkPattern().Match(message.TextBody);
        Assert.True(match.Success, $"No magic link in email '{message.Subject}'.");
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    /// <summary>Signs up an organization and completes the founder's sign-in. The domain is still unverified.</summary>
    public async Task<StaffActor> SignUpAsync(string domain = "acme.test", string founder = "priya", string name = "Acme Technologies")
    {
        var signUp = await RunAsync<OrganizationSignupService, Result<SignUpResult>>(
            s => s.SignUpAsync(new SignUpCommand(name, domain, $"{founder}@{domain}"), Token));
        Assert.True(signUp.Succeeded, signUp.Error?.Message);

        var token = LinkToken(Email.To($"{founder}@{domain}").Last());
        var session = await RunAsync<StaffAuthService, Result<StaffSession>>(s => s.CompleteSignInAsync(token, Token));
        Assert.True(session.Succeeded, session.Error?.Message);
        return new StaffActor(session.Value.MemberId, session.Value.OrganizationId);
    }

    /// <summary>Signs up and proves the domain with a DNS TXT record: an admin ready to issue.</summary>
    public async Task<StaffActor> CreateVerifiedOrganizationAsync(string domain = "acme.test", string founder = "priya", string name = "Acme Technologies")
    {
        var admin = await SignUpAsync(domain, founder, name);
        var status = await RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.GetStatusAsync(admin, Token));
        Dns.Publish(status.Value.DnsRecordName, status.Value.DnsRecordValue);
        var verified = await RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.CheckDnsAsync(admin, Token));
        Assert.True(verified.Succeeded, verified.Error?.Message);
        return admin;
    }

    public static IssueDocumentCommand SampleCommand(string employeeEmail = "anita.desai@example.org") => new(
        DocumentType.ExperienceLetter,
        "Anita Desai",
        employeeEmail,
        "Associate Engineer",
        new DateOnly(2019, 6, 1),
        new DateOnly(2024, 3, 31));

    public Task<Result<IssuedDocumentResult>> IssueAsync(StaffActor actor, IssueDocumentCommand? command = null, byte[]? pdf = null) =>
        RunAsync<DocumentIssuanceService, Result<IssuedDocumentResult>>(s =>
            s.IssueAsync(actor, command ?? SampleCommand(), new MemoryStream(pdf ?? SampleLetterGenerator.Generate()), Token));

    public async Task<IssuedDocumentResult> IssueSampleAsync(StaffActor actor, string employeeEmail = "anita.desai@example.org")
    {
        var result = await IssueAsync(actor, SampleCommand(employeeEmail));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value;
    }

    public async ValueTask DisposeAsync()
    {
        if (_dropDatabaseOnDispose)
            await WithDatabaseAsync(db => ((ChirographDbContext)db).Database.EnsureDeletedAsync());
        await _services.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [GeneratedRegex(@"/auth/link\?token=(?<token>[A-Za-z0-9_%\-]+)")]
    private static partial Regex MagicLinkPattern();
}
