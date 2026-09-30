using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Domain.Documents;
using Chirograph.Infrastructure.Pdf;
using Chirograph.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Chirograph.UnitTests.Application;

public class IssuanceTests : ApplicationTestBase
{
    [Fact]
    public async Task Issuing_stamps_the_letter_records_its_fingerprint_stores_it_and_emails_it()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var original = SampleLetterGenerator.Generate();

        var result = await Host.IssueAsync(admin, pdf: original);

        Assert.True(result.Succeeded, result.Error?.Message);
        var issued = result.Value;
        Assert.Equal(new Uri($"{ApplicationTestHost.PublicBaseUrl}/v/{issued.VerificationId}"), issued.VerificationUrl);
        Assert.Equal($"experience-letter-{issued.VerificationId}.pdf", issued.FileName);

        // The record holds the SHA-256 of exactly the stamped bytes handed out.
        var document = await Host.WithDatabaseAsync(db => db.Documents.SingleAsync(Token));
        Assert.Equal(DocumentFingerprint.Compute(issued.StampedPdf), document.Fingerprint);
        Assert.Equal(issued.StampedPdf.Length, document.FileSizeBytes);
        Assert.Equal("Anita Desai", document.EmployeeName);
        Assert.Equal("anita.desai@example.org", document.EmployeeEmail.Value);
        Assert.Equal("Associate Engineer", document.Designation);
        Assert.Equal(new DateOnly(2019, 6, 1), document.EmploymentStart);
        Assert.Equal(new DateOnly(2024, 3, 31), document.EmploymentEnd);

        // Only the stamped file is stored; the original upload is not kept.
        var stored = Assert.Single(Host.Files.Files);
        Assert.Equal(issued.StampedPdf, stored.Value);
        Assert.NotEqual(original, stored.Value);

        // The stamped file carries the verification ID.
        using (var pdf = PdfPigDocument.Open(issued.StampedPdf))
            Assert.Contains(issued.VerificationId.ToDisplayString(), pdf.GetPage(1).Text, StringComparison.Ordinal);

        // The employee receives exactly that file, the verification link and a portal link.
        Assert.True(issued.EmailedToEmployee);
        var email = Assert.Single(Host.Email.To("anita.desai@example.org"));
        Assert.Equal(issued.StampedPdf, Assert.Single(email.Attachments).Content);
        Assert.Contains(issued.VerificationUrl.AbsoluteUri, email.TextBody, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(ApplicationTestHost.LinkToken(email)));
    }

    [Fact]
    public async Task The_printed_QR_code_opens_the_configured_public_verification_link()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var issued = await Host.IssueSampleAsync(admin);

        var code = Assert.Single(PdfRendering.ReadQrCodes(issued.StampedPdf));
        Assert.Equal(issued.VerificationUrl.AbsoluteUri, code.Text);
    }

    [Fact]
    public async Task Every_letter_gets_its_own_verification_id_and_fingerprint()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var first = await Host.IssueSampleAsync(admin);
        var second = await Host.IssueSampleAsync(admin);

        Assert.NotEqual(first.VerificationId, second.VerificationId);
        Assert.NotEqual(DocumentFingerprint.Compute(first.StampedPdf), DocumentFingerprint.Compute(second.StampedPdf));
    }

    [Fact]
    public async Task An_unverified_organization_cannot_issue()
    {
        var founder = await Host.SignUpAsync();

        var result = await Host.IssueAsync(founder);

        Assert.Equal("organization.not_verified", result.Error?.Code);
        Assert.Empty(Host.Files.Files);
        Assert.Empty(Host.Email.To("anita.desai@example.org"));
    }

    [Fact]
    public async Task Invalid_details_are_rejected_before_anything_is_stored()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var command = ApplicationTestHost.SampleCommand() with { EmploymentStart = new DateOnly(2024, 5, 1), EmploymentEnd = new DateOnly(2024, 4, 1) };

        var result = await Host.IssueAsync(admin, command);

        Assert.Equal("document.invalid_dates", result.Error?.Code);
        Assert.Empty(Host.Files.Files);
        Assert.Equal(0, await Host.WithDatabaseAsync(db => db.Documents.CountAsync(Token)));
    }

    [Fact]
    public async Task The_employee_email_must_be_valid()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var result = await Host.IssueAsync(admin, ApplicationTestHost.SampleCommand("anita at example"));

        Assert.Equal("validation", result.Error?.Code);
    }

    [Fact]
    public async Task Files_that_are_not_PDFs_are_rejected()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var result = await Host.IssueAsync(admin, pdf: "Dear Sir or Madam, ..."u8.ToArray());

        Assert.Equal("upload.not_pdf", result.Error?.Code);
    }

    [Fact]
    public async Task Password_protected_PDFs_are_rejected_with_an_explanation()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();

        var result = await Host.IssueAsync(admin, pdf: TestPdfs.Encrypted("secret", "owner"));

        Assert.Equal("document.unusable_pdf", result.Error?.Code);
        Assert.Contains("password-protected", result.Error?.Message, StringComparison.Ordinal);
        Assert.Empty(Host.Files.Files);
    }

    [Fact]
    public async Task Oversized_uploads_are_rejected()
    {
        await using var host = await ApplicationTestHost.CreateAsync(options => options.MaxUploadBytes = 16 * 1024);
        var admin = await host.CreateVerifiedOrganizationAsync();

        var result = await host.IssueAsync(admin, pdf: SampleLetterGenerator.Generate());

        Assert.Equal("upload.too_large", result.Error?.Code);
    }

    [Fact]
    public async Task The_document_is_issued_even_if_the_email_cannot_be_sent()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        Host.Email.Fail = true;

        var result = await Host.IssueAsync(admin);

        Assert.True(result.Succeeded);
        Assert.False(result.Value.EmailedToEmployee);
        Assert.Equal(1, await Host.WithDatabaseAsync(db => db.Documents.CountAsync(Token)));
    }

    [Fact]
    public async Task The_letter_can_be_emailed_to_the_employee_again()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        var resent = await Host.RunAsync<DocumentIssuanceService, Result>(s => s.ResendToEmployeeAsync(admin, issued.DocumentId, Token));

        Assert.True(resent.Succeeded);
        var emails = Host.Email.To("anita.desai@example.org");
        Assert.Equal(2, emails.Count);
        Assert.Equal(issued.StampedPdf, emails[^1].Attachments.Single().Content);
    }
}
