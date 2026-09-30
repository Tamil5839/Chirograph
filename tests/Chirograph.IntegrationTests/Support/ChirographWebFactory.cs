using System.Text.RegularExpressions;
using Chirograph.Application.Abstractions;
using Chirograph.Application.Access;
using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Documents;
using Chirograph.Infrastructure.Pdf;
using Chirograph.UnitTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chirograph.IntegrationTests.Support;

/// <summary>
/// Hosts the real web app (real configuration, SQLite database, file storage, PDF stamping, auth and rate limiting)
/// in a private temporary folder. Only email and DNS are replaced, so tests can read links and publish records.
/// </summary>
public partial class ChirographWebFactory : WebApplicationFactory<Program>
{
    public const string PublicBaseUrl = "https://localhost";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chirograph-web-tests", Guid.NewGuid().ToString("N"));

    internal CapturingEmailSender Email { get; } = new();

    internal FakeDnsTxtResolver Dns { get; } = new();

    public string StorageRoot => Path.Combine(_root, "files");

    /// <summary>Settings applied on top of appsettings.json; subclasses may add more before the host starts.</summary>
    protected virtual IDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["ConnectionStrings:Chirograph"] = $"Data Source={Path.Combine(_root, "chirograph.db")}",
        ["Database:ApplyMigrationsOnStartup"] = "true",
        ["Storage:RootPath"] = StorageRoot,
        ["Email:OutboxDirectory"] = Path.Combine(_root, "outbox"),
        ["DataProtection:KeysPath"] = Path.Combine(_root, "keys"),
        ["Chirograph:PublicBaseUrl"] = PublicBaseUrl,
        ["RateLimits:VerificationViewsPerMinute"] = "100000",
        ["RateLimits:FileChecksPerTenMinutes"] = "100000",
        ["RateLimits:EmailRequestsPerFifteenMinutes"] = "100000",
        ["RateLimits:IssuesPerHour"] = "100000",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<IDnsTxtResolver>();
            services.AddSingleton<IDnsTxtResolver>(Dns);
        });
    }

    /// <summary>A browser-like client with its own cookie jar that follows redirects.</summary>
    public Browser CreateBrowser(bool followRedirects = true) =>
        new(CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(PublicBaseUrl),
            AllowAutoRedirect = followRedirects,
            HandleCookies = true,
        }));

    public static string UniqueDomain() => $"acme-{Guid.NewGuid():N}"[..13] + ".test";

    /// <summary>
    /// The magic link in the newest email to <paramref name="recipient"/> that has one and whose subject contains
    /// <paramref name="subject"/>.
    /// </summary>
    public string LatestLinkTo(string recipient, string subject = "")
    {
        var message = Email.To(recipient).LastOrDefault(m =>
            m.Subject.Contains(subject, StringComparison.Ordinal) && MagicLinkPattern().IsMatch(m.TextBody));
        Assert.NotNull(message);
        return MagicLinkPattern().Match(message.TextBody).Value;
    }

    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>
    /// Creates a verified organization and issues the sample letter through the application services, for tests
    /// that exercise only the public pages.
    /// </summary>
    public async Task<(StaffActor Admin, IssuedDocumentResult Document)> SeedIssuedDocumentAsync(string? domain = null)
    {
        domain ??= UniqueDomain();
        var token = TestContext.Current.CancellationToken;
        var signUp = await RunAsync<OrganizationSignupService, Result<SignUpResult>>(
            s => s.SignUpAsync(new SignUpCommand("Acme Technologies", domain, $"priya@{domain}"), token));
        Assert.True(signUp.Succeeded, signUp.Error?.Message);

        var link = new Uri(LatestLinkTo($"priya@{domain}"));
        var rawToken = Uri.UnescapeDataString(link.Query["?token=".Length..]);
        var session = await RunAsync<StaffAuthService, Result<StaffSession>>(s => s.CompleteSignInAsync(rawToken, token));
        var admin = new StaffActor(session.Value.MemberId, session.Value.OrganizationId);

        var status = await RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.GetStatusAsync(admin, token));
        Dns.Publish(status.Value.DnsRecordName, status.Value.DnsRecordValue);
        var verified = await RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(s => s.CheckDnsAsync(admin, token));
        Assert.True(verified.Succeeded, verified.Error?.Message);

        var issued = await RunAsync<DocumentIssuanceService, Result<IssuedDocumentResult>>(s => s.IssueAsync(
            admin,
            new IssueDocumentCommand(DocumentType.ExperienceLetter, "Anita Desai", "anita.desai@example.org", "Associate Engineer",
                new DateOnly(2019, 6, 1), new DateOnly(2024, 3, 31)),
            new MemoryStream(SampleLetterGenerator.Generate()),
            token));
        Assert.True(issued.Succeeded, issued.Error?.Message);
        return (admin, issued.Value);
    }

    /// <summary>Signs an HR member in the way a person would: request a link on the sign-in page, then follow it.</summary>
    public async Task<HtmlPage> SignInStaffAsync(Browser browser, string email)
    {
        await browser.SubmitAsync(await browser.GetAsync("/auth/signin"), "#signin-form", new Dictionary<string, string> { ["Email"] = email });
        return await browser.SubmitAsync(await browser.GetAsync(LatestLinkTo(email, "sign-in link")), "#link-form");
    }

    /// <summary>Signs an employee in from the "My documents" page.</summary>
    public async Task<HtmlPage> SignInEmployeeAsync(Browser browser, string email)
    {
        await browser.SubmitAsync(await browser.GetAsync("/me"), "#employee-signin-form", new Dictionary<string, string> { ["Email"] = email });
        return await browser.SubmitAsync(await browser.GetAsync(LatestLinkTo(email, "documents sign-in link")), "#link-form");
    }

    /// <summary>Invites a teammate (via the application) and accepts the invitation in <paramref name="browser"/>.</summary>
    public async Task<StaffActor> AddTeammateAsync(StaffActor admin, Browser browser, string email, Chirograph.Domain.Organizations.MemberRole role)
    {
        var invited = await RunAsync<TeamService, Result>(s => s.InviteAsync(admin, email, role, TestContext.Current.CancellationToken));
        Assert.True(invited.Succeeded, invited.Error?.Message);
        await browser.SubmitAsync(await browser.GetAsync(LatestLinkTo(email, "invited")), "#link-form");
        var member = await RunAsync<Chirograph.Application.Organizations.TeamService, Result<IReadOnlyList<TeamMemberView>>>(
            s => s.ListAsync(admin, TestContext.Current.CancellationToken));
        return new StaffActor(member.Value.Single(m => m.Email.Value == email).Id, admin.OrganizationId);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [GeneratedRegex(@"https://localhost/auth/link\?token=[A-Za-z0-9_%\-]+")]
    private static partial Regex MagicLinkPattern();
}
