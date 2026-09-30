using System.Net;
using Chirograph.Application.Common;
using Chirograph.Application.Organizations;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Infrastructure.Pdf;
using Chirograph.IntegrationTests.Support;

namespace Chirograph.IntegrationTests;

public sealed class AccessControlTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    [Theory]
    [InlineData("/org")]
    [InlineData("/org/domain")]
    [InlineData("/org/team")]
    [InlineData("/org/documents/issue")]
    public async Task HR_pages_require_signing_in(string path)
    {
        using var anonymous = factory.CreateBrowser(followRedirects: false);

        using var response = await anonymous.GetRawAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/auth/signin", response.Headers.Location?.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_pages_send_visitors_to_the_employee_sign_in()
    {
        using var anonymous = factory.CreateBrowser(followRedirects: false);

        using var response = await anonymous.GetRawAsync($"/me/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/me", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Issuers_cannot_manage_the_team()
    {
        var (admin, _) = await factory.SeedIssuedDocumentAsync();
        var domain = (await factory.RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(
            s => s.GetStatusAsync(admin, TestContext.Current.CancellationToken))).Value.Domain;
        using var issuer = factory.CreateBrowser();
        await factory.AddTeammateAsync(admin, issuer, $"ravi@{domain}", MemberRole.Issuer);

        var page = await issuer.GetAsync("/org/team");

        Assert.Equal("/auth/denied", page.Url.AbsolutePath);
    }

    [Fact]
    public async Task One_organization_cannot_see_anothers_documents()
    {
        var (_, acmeDocument) = await factory.SeedIssuedDocumentAsync();
        var globexDomain = ChirographWebFactory.UniqueDomain();
        await factory.SeedIssuedDocumentAsync(globexDomain);
        using var globex = factory.CreateBrowser();
        await factory.SignInStaffAsync(globex, $"priya@{globexDomain}");

        var details = await globex.GetAsync($"/org/documents/{acmeDocument.DocumentId}");
        using var download = await globex.GetRawAsync($"/org/documents/{acmeDocument.DocumentId}?handler=Download");
        var dashboard = await globex.GetAsync("/org");

        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Contains("Anita Desai", dashboard.Text, StringComparison.Ordinal); // Globex's own letter is listed…
        Assert.Null(dashboard.Document.QuerySelector($"a[href='/org/documents/{acmeDocument.DocumentId}']")); // …Acme's is not
    }

    [Fact]
    public async Task Employees_cannot_use_HR_pages()
    {
        await factory.SeedIssuedDocumentAsync();
        using var employee = factory.CreateBrowser();
        await factory.SignInEmployeeAsync(employee, "anita.desai@example.org");

        var page = await employee.GetAsync("/org");

        Assert.Equal("/auth/denied", page.Url.AbsolutePath);
    }

    [Fact]
    public async Task An_organization_cannot_issue_before_verifying_its_domain()
    {
        var domain = ChirographWebFactory.UniqueDomain();
        using var hr = factory.CreateBrowser();
        await hr.SubmitAsync(await hr.GetAsync("/signup"), "#signup-form", new Dictionary<string, string>
        {
            ["Input.OrganizationName"] = "Acme Technologies",
            ["Input.Domain"] = domain,
            ["Input.Email"] = $"priya@{domain}",
        });
        await hr.SubmitAsync(await hr.GetAsync(factory.LatestLinkTo($"priya@{domain}")), "#link-form");
        var filesBefore = Directory.Exists(factory.StorageRoot) ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length : 0;

        var form = await hr.GetAsync("/org/documents/issue");
        Assert.NotNull(form.Document.QuerySelector("#issue-form button[disabled]"));
        var result = await hr.SubmitAsync(
            form,
            "#issue-form",
            new Dictionary<string, string>
            {
                ["Input.Type"] = nameof(DocumentType.RelievingLetter),
                ["Input.EmployeeName"] = "Anita Desai",
                ["Input.Designation"] = "Associate Engineer",
                ["Input.EmployeeEmail"] = "anita.desai@example.org",
                ["Input.EmploymentStart"] = "2019-06-01",
                ["Input.EmploymentEnd"] = "2024-03-31",
            },
            new Dictionary<string, (string, byte[])> { ["Input.Pdf"] = ("letter.pdf", SampleLetterGenerator.Generate()) });

        Assert.Contains("Verify your organization's domain before issuing documents.", result.Text, StringComparison.Ordinal);
        var filesAfter = Directory.Exists(factory.StorageRoot) ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length : 0;
        Assert.Equal(filesBefore, filesAfter);
    }

    [Fact]
    public async Task A_removed_member_is_signed_out_immediately()
    {
        var (admin, _) = await factory.SeedIssuedDocumentAsync();
        var domain = (await factory.RunAsync<DomainVerificationService, Result<DomainVerificationStatus>>(
            s => s.GetStatusAsync(admin, TestContext.Current.CancellationToken))).Value.Domain;
        using var ravi = factory.CreateBrowser();
        var raviActor = await factory.AddTeammateAsync(admin, ravi, $"ravi@{domain}", MemberRole.Issuer);
        Assert.Equal("/org", (await ravi.GetAsync("/org")).Url.AbsolutePath);

        await factory.RunAsync<TeamService, Result>(s => s.RemoveAsync(admin, raviActor.MemberId, TestContext.Current.CancellationToken));

        Assert.Equal("/auth/signin", (await ravi.GetAsync("/org")).Url.AbsolutePath);
    }
}
