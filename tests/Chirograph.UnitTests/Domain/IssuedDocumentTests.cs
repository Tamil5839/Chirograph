using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Domain;

public class IssuedDocumentTests
{
    private readonly Organization _organization = TestData.VerifiedOrganization();
    private readonly OrganizationMember _admin;
    private readonly OrganizationMember _issuer;

    public IssuedDocumentTests()
    {
        _admin = TestData.ActiveAdmin(_organization);
        _issuer = TestData.ActiveIssuer(_organization, _admin);
    }

    [Fact]
    public void Issuing_records_the_fields_and_the_fingerprint_of_the_file()
    {
        var content = "%PDF-1.7 stamped letter"u8.ToArray();
        var id = VerificationId.New();

        var document = IssuedDocument.Issue(
            _organization, _issuer, TestData.Details(), id, DocumentFingerprint.Compute(content), content.Length,
            "documents/abc.pdf", TestData.Now);

        Assert.Equal(DocumentStatus.Valid, document.Status);
        Assert.Equal(id, document.VerificationId);
        Assert.Equal(_organization.Id, document.OrganizationId);
        Assert.Equal(_issuer.Id, document.IssuedByMemberId);
        Assert.Equal(DocumentType.ExperienceLetter, document.Type);
        Assert.Equal("Anita Desai", document.EmployeeName);
        Assert.Equal("anita.desai@example.org", document.EmployeeEmail.Value);
        Assert.Equal("Associate Engineer", document.Designation);
        Assert.Equal(new DateOnly(2019, 6, 1), document.EmploymentStart);
        Assert.Equal(new DateOnly(2024, 3, 31), document.EmploymentEnd);
        Assert.Equal(TestData.Now, document.IssuedAt);
        Assert.Equal(DocumentFingerprint.Compute(content), document.Fingerprint);
        Assert.Equal(content.Length, document.FileSizeBytes);
        Assert.Null(document.RevokedAt);
    }

    [Fact]
    public void An_unverified_organization_cannot_issue()
    {
        var pending = TestData.PendingOrganization();
        var founder = TestData.ActiveAdmin(pending);

        var error = Assert.Throws<DomainException>(() => TestData.IssuedDocument(pending, founder));

        Assert.Equal("organization.not_verified", error.Code);
    }

    [Fact]
    public void A_member_who_has_not_confirmed_their_mailbox_cannot_issue()
    {
        var invited = OrganizationMember.Invite(
            _organization, _admin, TestData.Email("meera@acme.test"), MemberRole.Issuer, TestData.Now);

        var error = Assert.Throws<DomainException>(() => TestData.IssuedDocument(_organization, invited));

        Assert.Equal("member.inactive", error.Code);
    }

    [Fact]
    public void A_member_cannot_issue_on_behalf_of_another_organization()
    {
        var globex = TestData.VerifiedOrganization("globex.test");
        var globexAdmin = TestData.ActiveAdmin(globex, "hank");

        var error = Assert.Throws<DomainException>(() => TestData.IssuedDocument(_organization, globexAdmin));

        Assert.Equal("member.wrong_organization", error.Code);
    }

    [Fact]
    public void Employment_cannot_start_after_the_letter_is_issued()
    {
        var details = TestData.Details(
            start: DateOnly.FromDateTime(TestData.Now.UtcDateTime.AddDays(10)),
            end: DateOnly.FromDateTime(TestData.Now.UtcDateTime.AddDays(20)));

        var error = Assert.Throws<DomainException>(() => IssuedDocument.Issue(
            _organization, _issuer, details, VerificationId.New(), DocumentFingerprint.Compute([1]), 1, "k", TestData.Now));

        Assert.Equal("document.invalid_dates", error.Code);
    }

    [Fact]
    public void Details_must_have_an_end_date_on_or_after_the_start_date()
    {
        var error = Assert.Throws<DomainException>(() => TestData.Details(
            start: new DateOnly(2024, 1, 1), end: new DateOnly(2023, 12, 31)));

        Assert.Equal("document.invalid_dates", error.Code);
        Assert.Equal(new DateOnly(2024, 1, 1), TestData.Details(start: new DateOnly(2024, 1, 1), end: new DateOnly(2024, 1, 1)).EmploymentEnd);
    }

    [Theory]
    [InlineData("", "Engineer")]
    [InlineData("Anita Desai", "   ")]
    public void Details_require_name_and_designation(string name, string designation)
    {
        var error = Assert.Throws<DomainException>(() => TestData.Details(employeeName: name, designation: designation));

        Assert.Equal("validation.required", error.Code);
    }

    [Fact]
    public void Details_are_normalised_before_they_are_stored()
    {
        var details = TestData.Details(employeeName: "  Anita‮  Desai ", designation: "Senior\n Engineer");

        Assert.Equal("Anita Desai", details.EmployeeName);
        Assert.Equal("Senior Engineer", details.Designation);
    }

    [Fact]
    public void Revoking_records_who_when_and_why()
    {
        var document = TestData.IssuedDocument(_organization, _admin);
        var revokedAt = TestData.Now.AddDays(3);

        document.Revoke(_issuer, "  Issued with the wrong end date ", revokedAt);

        Assert.Equal(DocumentStatus.Revoked, document.Status);
        Assert.Equal(revokedAt, document.RevokedAt);
        Assert.Equal(_issuer.Id, document.RevokedByMemberId);
        Assert.Equal("Issued with the wrong end date", document.RevocationReason);
    }

    [Fact]
    public void A_document_cannot_be_revoked_twice()
    {
        var document = TestData.IssuedDocument(_organization, _admin);
        document.Revoke(_admin, "Issued in error", TestData.Now);

        var error = Assert.Throws<DomainException>(() => document.Revoke(_admin, "Again", TestData.Now.AddDays(1)));

        Assert.Equal("document.already_revoked", error.Code);
        Assert.Equal("Issued in error", document.RevocationReason);
        Assert.Equal(TestData.Now, document.RevokedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoking_requires_a_reason(string? reason)
    {
        var document = TestData.IssuedDocument(_organization, _admin);

        var error = Assert.Throws<DomainException>(() => document.Revoke(_admin, reason, TestData.Now));

        Assert.Equal("validation.required", error.Code);
        Assert.Equal(DocumentStatus.Valid, document.Status);
    }

    [Fact]
    public void The_revocation_reason_is_bounded()
    {
        var document = TestData.IssuedDocument(_organization, _admin);

        var error = Assert.Throws<DomainException>(
            () => document.Revoke(_admin, new string('x', IssuedDocument.RevocationReasonMaxLength + 1), TestData.Now));

        Assert.Equal("validation.too_long", error.Code);
    }

    [Fact]
    public void Another_organization_cannot_revoke()
    {
        var document = TestData.IssuedDocument(_organization, _admin);
        var outsider = TestData.ActiveAdmin(TestData.VerifiedOrganization("globex.test"), "hank");

        var error = Assert.Throws<DomainException>(() => document.Revoke(outsider, "Not ours", TestData.Now));

        Assert.Equal("member.wrong_organization", error.Code);
        Assert.Equal(DocumentStatus.Valid, document.Status);
    }

    [Fact]
    public void A_removed_member_cannot_revoke()
    {
        var document = TestData.IssuedDocument(_organization, _admin);
        _issuer.Remove(_organization, _admin, TestData.Now);

        var error = Assert.Throws<DomainException>(() => document.Revoke(_issuer, "Leaving", TestData.Now));

        Assert.Equal("member.inactive", error.Code);
    }
}
