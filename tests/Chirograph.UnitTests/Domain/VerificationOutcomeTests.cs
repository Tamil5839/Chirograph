using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.Domain.Verification;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Domain;

public class VerificationOutcomeTests
{
    private static readonly byte[] IssuedFile = "%PDF-1.7 the stamped letter as issued"u8.ToArray();

    private readonly Organization _organization = TestData.VerifiedOrganization();
    private readonly OrganizationMember _admin;
    private readonly IssuedDocument _document;

    public VerificationOutcomeTests()
    {
        _admin = TestData.ActiveAdmin(_organization);
        _document = TestData.IssuedDocument(_organization, _admin, IssuedFile);
    }

    private static DocumentFingerprint SameFile => DocumentFingerprint.Compute(IssuedFile);

    private static DocumentFingerprint TamperedFile
    {
        get
        {
            var tampered = (byte[])IssuedFile.Clone();
            tampered[^1] ^= 0x01;
            return DocumentFingerprint.Compute(tampered);
        }
    }

    [Fact]
    public void An_unknown_id_is_not_found()
    {
        var outcome = VerificationOutcome.Evaluate(null, null);

        Assert.Equal(VerificationVerdict.NotFound, outcome.Verdict);
        Assert.Equal(FileCheckResult.NotAttempted, outcome.FileCheck);
        Assert.True(outcome.Failed);
        Assert.False(outcome.Passed);
    }

    [Fact]
    public void An_unknown_id_is_not_found_even_when_a_file_is_presented()
    {
        var outcome = VerificationOutcome.Evaluate(null, SameFile);

        Assert.Equal(VerificationVerdict.NotFound, outcome.Verdict);
        Assert.True(outcome.Failed);
    }

    [Fact]
    public void A_valid_record_without_a_file_is_neither_a_pass_nor_a_failure()
    {
        var outcome = VerificationOutcome.Evaluate(_document, null);

        Assert.Equal(VerificationVerdict.RecordValid, outcome.Verdict);
        Assert.Equal(FileCheckResult.NotAttempted, outcome.FileCheck);
        Assert.False(outcome.Passed);
        Assert.False(outcome.Failed);
    }

    [Fact]
    public void The_exact_issued_file_is_authentic()
    {
        var outcome = VerificationOutcome.Evaluate(_document, SameFile);

        Assert.Equal(VerificationVerdict.Authentic, outcome.Verdict);
        Assert.Equal(FileCheckResult.Match, outcome.FileCheck);
        Assert.True(outcome.Passed);
        Assert.False(outcome.Failed);
    }

    [Fact]
    public void A_file_differing_by_one_bit_fails()
    {
        var outcome = VerificationOutcome.Evaluate(_document, TamperedFile);

        Assert.Equal(VerificationVerdict.FileMismatch, outcome.Verdict);
        Assert.Equal(FileCheckResult.Mismatch, outcome.FileCheck);
        Assert.False(outcome.Passed);
        Assert.True(outcome.Failed);
    }

    [Theory]
    [InlineData(FileCheckResult.NotAttempted)]
    [InlineData(FileCheckResult.Match)]
    [InlineData(FileCheckResult.Mismatch)]
    public void A_revoked_document_fails_whatever_file_is_presented(FileCheckResult presented)
    {
        var revokedAt = TestData.Now.AddDays(2);
        _document.Revoke(_admin, "Issued in error", revokedAt);
        var file = presented switch
        {
            FileCheckResult.Match => SameFile,
            FileCheckResult.Mismatch => TamperedFile,
            _ => null,
        };

        var outcome = VerificationOutcome.Evaluate(_document, file);

        Assert.Equal(VerificationVerdict.Revoked, outcome.Verdict);
        Assert.Equal(presented, outcome.FileCheck);
        Assert.Equal(revokedAt, outcome.RevokedAt);
        Assert.True(outcome.Failed);
        Assert.False(outcome.Passed);
    }

    [Fact]
    public void Opening_the_page_is_recorded_as_a_view()
    {
        var entry = VerificationEvent.Record(_document, VerificationOutcome.Evaluate(_document, null), null, TestData.Now);

        Assert.Equal(VerificationKind.Viewed, entry.Kind);
        Assert.Equal(_document.Id, entry.DocumentId);
        Assert.Equal(DocumentStatus.Valid, entry.StatusShown);
        Assert.Null(entry.FileMatched);
        Assert.Null(entry.DeclaredVerifier);
        Assert.Equal(TestData.Now, entry.OccurredAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_file_check_is_recorded_with_its_result(bool sameFile)
    {
        var outcome = VerificationOutcome.Evaluate(_document, sameFile ? SameFile : TamperedFile);

        var entry = VerificationEvent.Record(_document, outcome, "  Globex   Background Checks ", TestData.Now);

        Assert.Equal(VerificationKind.FileChecked, entry.Kind);
        Assert.Equal(sameFile, entry.FileMatched);
        Assert.Equal("Globex Background Checks", entry.DeclaredVerifier);
    }

    [Fact]
    public void The_status_shown_is_recorded_for_revoked_documents()
    {
        _document.Revoke(_admin, "Issued in error", TestData.Now);

        var entry = VerificationEvent.Record(_document, VerificationOutcome.Evaluate(_document, SameFile), null, TestData.Now);

        Assert.Equal(DocumentStatus.Revoked, entry.StatusShown);
        Assert.True(entry.FileMatched);
    }

    [Fact]
    public void A_declared_verifier_name_is_bounded()
    {
        var entry = VerificationEvent.Record(
            _document, VerificationOutcome.Evaluate(_document, null), new string('x', 500), TestData.Now);

        Assert.Equal(VerificationEvent.DeclaredVerifierMaxLength, entry.DeclaredVerifier!.Length);
    }

    [Fact]
    public void A_lookup_that_found_nothing_cannot_be_recorded_against_a_document()
    {
        Assert.Throws<ArgumentException>(() => VerificationEvent.Record(
            _document, VerificationOutcome.Evaluate(null, null), null, TestData.Now));
    }
}
