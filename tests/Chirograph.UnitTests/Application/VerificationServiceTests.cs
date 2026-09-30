using Chirograph.Application.Common;
using Chirograph.Application.Documents;
using Chirograph.Application.Verification;
using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Chirograph.Infrastructure.Pdf;
using Chirograph.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Chirograph.UnitTests.Application;

public class VerificationServiceTests : ApplicationTestBase
{
    private Task<VerificationResult> View(string id) =>
        Host.RunAsync<VerificationService, VerificationResult>(s => s.ViewAsync(id, Token));

    private Task<VerificationResult> Check(string id, byte[] file, string? verifier = null) =>
        Host.RunAsync<VerificationService, VerificationResult>(s => s.CheckFileAsync(id, new MemoryStream(file), verifier, Token));

    [Fact]
    public async Task Opening_the_link_shows_the_issuer_its_verified_domain_and_the_letter_details()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        var result = await View(issued.VerificationId.Value);

        Assert.Equal(VerificationVerdict.RecordValid, result.Outcome.Verdict);
        var document = Assert.IsType<PublicDocumentView>(result.Document);
        Assert.Equal("Acme Technologies", document.IssuerName);
        Assert.Equal("acme.test", document.IssuerDomain.Value);
        Assert.Equal(DomainVerificationMethod.DnsTxtRecord, document.IssuerVerificationMethod);
        Assert.Equal(DocumentType.ExperienceLetter, document.Type);
        Assert.Equal("Anita Desai", document.EmployeeName);
        Assert.Equal("Associate Engineer", document.Designation);
        Assert.Equal(new DateOnly(2019, 6, 1), document.EmploymentStart);
        Assert.Equal(new DateOnly(2024, 3, 31), document.EmploymentEnd);
        Assert.Equal(TestData.Now, document.IssuedAt);
        Assert.Equal(DocumentStatus.Valid, document.Status);
        Assert.Equal(DocumentFingerprint.Compute(issued.StampedPdf), document.Fingerprint);
    }

    [Fact]
    public async Task The_exact_issued_file_passes()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        var result = await Check(issued.VerificationId.Value, issued.StampedPdf);

        Assert.Equal(VerificationVerdict.Authentic, result.Outcome.Verdict);
        Assert.True(result.Outcome.Passed);
        Assert.Equal(result.Document?.Fingerprint, result.PresentedFingerprint);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("middle")]
    [InlineData("last")]
    public async Task A_file_with_a_single_changed_byte_fails(string where)
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        var tampered = issued.StampedPdf.ToArray();
        var index = where switch { "first" => 0, "middle" => tampered.Length / 2, _ => tampered.Length - 1 };
        tampered[index] ^= 0x01;

        var result = await Check(issued.VerificationId.Value, tampered);

        Assert.Equal(VerificationVerdict.FileMismatch, result.Outcome.Verdict);
        Assert.True(result.Outcome.Failed);
        Assert.NotEqual(result.Document?.Fingerprint, result.PresentedFingerprint);
    }

    [Fact]
    public async Task A_convincing_edit_to_the_text_fails()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        // Same-length edit of the visible designation: the PDF stays valid and reads "Principal Engineer".
        var forged = System.Text.Encoding.Latin1.GetBytes(
            System.Text.Encoding.Latin1.GetString(issued.StampedPdf).Replace("Associate", "Principal", StringComparison.Ordinal));
        Assert.Equal(issued.StampedPdf.Length, forged.Length);
        Assert.NotEqual(issued.StampedPdf, forged);

        var result = await Check(issued.VerificationId.Value, forged);

        Assert.Equal(VerificationVerdict.FileMismatch, result.Outcome.Verdict);
    }

    [Fact]
    public async Task The_original_unstamped_upload_does_not_pass()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var original = SampleLetterGenerator.Generate();
        var issued = (await Host.IssueAsync(admin, pdf: original)).Value;

        var result = await Check(issued.VerificationId.Value, original);

        Assert.Equal(VerificationVerdict.FileMismatch, result.Outcome.Verdict);
    }

    [Fact]
    public async Task Every_check_is_logged_for_the_issuer()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);
        var id = issued.VerificationId.Value;

        await View(id);
        Host.Time.Advance(TimeSpan.FromMinutes(1));
        await Check(id, issued.StampedPdf, "  Globex   HR ");
        Host.Time.Advance(TimeSpan.FromMinutes(1));
        await Check(id, [1, 2, 3]);

        var view = await Host.RunAsync<IssuerDocumentsService, Result<IssuerDocumentView>>(s => s.GetAsync(admin, issued.DocumentId, Token));
        Assert.Collection(
            view.Value.Verifications,
            mismatch =>
            {
                Assert.Equal(VerificationKind.FileChecked, mismatch.Kind);
                Assert.False(mismatch.FileMatched);
                Assert.Null(mismatch.DeclaredVerifier);
            },
            match =>
            {
                Assert.Equal(VerificationKind.FileChecked, match.Kind);
                Assert.True(match.FileMatched);
                Assert.Equal("Globex HR", match.DeclaredVerifier);
                Assert.Equal(TestData.Now.AddMinutes(1), match.OccurredAt);
            },
            opened =>
            {
                Assert.Equal(VerificationKind.Viewed, opened.Kind);
                Assert.Null(opened.FileMatched);
                Assert.Equal(DocumentStatus.Valid, opened.StatusShown);
            });
    }

    [Fact]
    public async Task Ids_typed_by_hand_are_found()
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        var issued = await Host.IssueSampleAsync(admin);

        var result = await View($" {issued.VerificationId.ToDisplayString().ToLowerInvariant()} ");

        Assert.Equal(VerificationVerdict.RecordValid, result.Outcome.Verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-verification-id")]
    [InlineData("0000000000000000000000000")]
    public async Task Unknown_ids_are_not_found_and_nothing_is_logged(string id)
    {
        var admin = await Host.CreateVerifiedOrganizationAsync();
        await Host.IssueSampleAsync(admin);

        var viewed = await View(id);
        var checkedFile = await Check(id, [1, 2, 3]);

        Assert.Equal(VerificationVerdict.NotFound, viewed.Outcome.Verdict);
        Assert.Equal(VerificationVerdict.NotFound, checkedFile.Outcome.Verdict);
        Assert.Null(viewed.Document);
        Assert.Equal(0, await Host.WithDatabaseAsync(db => db.VerificationEvents.CountAsync(Token)));
    }

    [Fact]
    public void The_public_view_cannot_carry_the_employee_email_or_the_revocation_reason()
    {
        var properties = typeof(PublicDocumentView).GetProperties();

        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(EmailAddress));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase));
    }
}
