using System.Net;
using Chirograph.Domain.Documents;
using Chirograph.Infrastructure.Pdf;
using Chirograph.IntegrationTests.Support;
using Chirograph.UnitTests.Support;

namespace Chirograph.IntegrationTests;

/// <summary>
/// The README demo script, driven through the real pages and forms: register → confirm email → verify domain →
/// issue → verify → tamper → revoke → employee sees who verified.
/// </summary>
public sealed class EndToEndTests(ChirographWebFactory factory) : IClassFixture<ChirographWebFactory>
{
    [Fact]
    public async Task The_demo_script_works_end_to_end()
    {
        var domain = ChirographWebFactory.UniqueDomain();
        var founder = $"priya@{domain}";
        using var hr = factory.CreateBrowser();

        // 1. Register the organization; the founder confirms their mailbox and lands on domain verification.
        var signUp = await hr.SubmitAsync(await hr.GetAsync("/signup"), "#signup-form", new Dictionary<string, string>
        {
            ["Input.OrganizationName"] = "Acme Technologies",
            ["Input.Domain"] = domain,
            ["Input.Email"] = founder,
        });
        Assert.Contains("Check your inbox", signUp.Text, StringComparison.Ordinal);
        var confirmation = await hr.GetAsync(factory.LatestLinkTo(founder));
        var domainPage = await hr.SubmitAsync(confirmation, "#link-form");
        Assert.Equal("/org/domain", domainPage.Url.AbsolutePath);
        Assert.Null(domainPage.Document.QuerySelector("a[href='/org/documents/issue']")); // can't issue yet

        // 2. Verify the domain from its administrative mailbox.
        var sent = await hr.SubmitAsync(domainPage, "#send-email-form", new Dictionary<string, string> { ["Mailbox"] = "admin" });
        Assert.Contains($"admin@{domain}", sent.Text, StringComparison.Ordinal);
        using (var domainAdmin = factory.CreateBrowser())
        {
            var confirm = await domainAdmin.GetAsync(factory.LatestLinkTo($"admin@{domain}"));
            Assert.Contains(founder, confirm.Text, StringComparison.Ordinal);
            var confirmed = await domainAdmin.SubmitAsync(confirm, "#link-form");
            Assert.Contains("Domain confirmed", confirmed.Text, StringComparison.Ordinal);
        }

        // 3. Issue a letter.
        var issueForm = await hr.GetAsync("/org/documents/issue");
        var details = await hr.SubmitAsync(
            issueForm,
            "#issue-form",
            new Dictionary<string, string>
            {
                ["Input.Type"] = nameof(DocumentType.ExperienceLetter),
                ["Input.EmployeeName"] = "Anita Desai",
                ["Input.Designation"] = "Associate Engineer",
                ["Input.EmployeeEmail"] = "anita.desai@example.org",
                ["Input.EmploymentStart"] = "2019-06-01",
                ["Input.EmploymentEnd"] = "2024-03-31",
            },
            new Dictionary<string, (string, byte[])> { ["Input.Pdf"] = ("letter.pdf", SampleLetterGenerator.Generate()) });
        Assert.Matches("^/org/documents/[0-9a-f-]{36}$", details.Url.AbsolutePath);
        Assert.Contains("Issued and emailed", details.Text, StringComparison.Ordinal);
        var verificationUrl = details.TextOf("#verification-url")!;

        using var download = await hr.GetRawAsync(details.Url.AbsolutePath + "?handler=Download");
        var stamped = await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(verificationUrl, Assert.Single(PdfRendering.ReadQrCodes(stamped)).Text);
        var emailed = factory.Email.To("anita.desai@example.org").Single(m => m.Attachments.Count == 1);
        Assert.Equal(stamped, emailed.Attachments[0].Content);

        // 4. A verifier scans the QR code and checks the file.
        using var verifier = factory.CreateBrowser();
        var record = await verifier.GetAsync(verificationUrl);
        Assert.Equal("record-valid", record.AttributeOf("#verdict", "data-verdict"));
        Assert.Equal(domain, record.TextOf("#issuer-domain"));
        var genuine = await verifier.SubmitAsync(
            record, "#check-form", new Dictionary<string, string> { ["VerifierOrganization"] = "Globex Background Checks" },
            new Dictionary<string, (string, byte[])> { ["Upload"] = ("letter.pdf", stamped) });
        Assert.Equal("authentic", genuine.AttributeOf("#verdict", "data-verdict"));

        // 5. Tamper with one byte: verification fails.
        var tampered = stamped.ToArray();
        tampered[tampered.Length / 2] ^= 0x01;
        var rejected = await verifier.SubmitAsync(
            genuine, "#check-form", files: new Dictionary<string, (string, byte[])> { ["Upload"] = ("letter.pdf", tampered) });
        Assert.Equal("file-mismatch", rejected.AttributeOf("#verdict", "data-verdict"));

        // 6. The issuer revokes the letter; verifiers now see it as revoked.
        var revoked = await hr.SubmitAsync(
            await hr.GetAsync(details.Url.AbsolutePath), "#revoke-form", new Dictionary<string, string> { ["Reason"] = "Issued with the wrong end date" });
        Assert.Contains("Document revoked", revoked.Text, StringComparison.Ordinal);
        Assert.Equal("revoked", (await verifier.GetAsync(verificationUrl)).AttributeOf("#verdict", "data-verdict"));

        // 7. The employee signs in from the issue email and sees the letter, the reason and who verified it.
        using var employee = factory.CreateBrowser();
        var portalLink = await employee.GetAsync(factory.LatestLinkTo("anita.desai@example.org", "Your experience letter from"));
        var myDocuments = await employee.SubmitAsync(portalLink, "#link-form");
        Assert.Equal("/me", myDocuments.Url.AbsolutePath);
        var documentLink = myDocuments.AttributeOf(".list-group a", "href")!;
        var myDocument = await employee.GetAsync(documentLink);
        Assert.Equal(HttpStatusCode.OK, myDocument.StatusCode);
        Assert.Equal("Issued with the wrong end date", myDocument.TextOf("#revocation-reason"));
        Assert.Contains("Globex Background Checks", myDocument.Text, StringComparison.Ordinal);
        Assert.Contains("File checked: exact match", myDocument.Text, StringComparison.Ordinal);
        Assert.Contains("File checked: did not match", myDocument.Text, StringComparison.Ordinal);
    }
}
