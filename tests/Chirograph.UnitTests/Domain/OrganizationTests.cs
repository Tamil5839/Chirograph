using Chirograph.Domain.Common;
using Chirograph.Domain.Organizations;
using Chirograph.UnitTests.Support;

namespace Chirograph.UnitTests.Domain;

public class OrganizationTests
{
    [Fact]
    public void A_new_organization_is_pending_with_a_random_dns_challenge()
    {
        var first = TestData.PendingOrganization();
        var second = TestData.PendingOrganization();

        Assert.Equal(OrganizationStatus.PendingVerification, first.Status);
        Assert.False(first.IsVerified);
        Assert.Matches("^[0-9a-f]{32}$", first.DnsChallenge);
        Assert.NotEqual(first.DnsChallenge, second.DnsChallenge);
        Assert.Equal("_chirograph.acme.test", first.DnsRecordName);
        Assert.Equal("chirograph-verification=" + first.DnsChallenge, first.DnsRecordValue);
    }

    [Fact]
    public void The_display_name_is_normalised()
    {
        var organization = Organization.Register("  Acme   Technologies‮ ", TestData.Domain(), TestData.Now);

        Assert.Equal("Acme Technologies", organization.Name);
    }

    [Fact]
    public void Public_email_domains_cannot_register()
    {
        var error = Assert.Throws<DomainException>(() => TestData.PendingOrganization("gmail.com"));

        Assert.Equal("organization.public_email_domain", error.Code);
    }

    [Fact]
    public void An_unverified_organization_cannot_issue_documents()
    {
        var error = Assert.Throws<DomainException>(() => TestData.PendingOrganization().EnsureCanIssueDocuments());

        Assert.Equal("organization.not_verified", error.Code);
    }

    [Fact]
    public void Marking_verified_records_how_and_when()
    {
        var organization = TestData.PendingOrganization();

        organization.MarkVerified(DomainVerificationMethod.AdminMailbox, "Confirmed by admin@acme.test", TestData.Now);

        Assert.True(organization.IsVerified);
        Assert.Equal(DomainVerificationMethod.AdminMailbox, organization.VerificationMethod);
        Assert.Equal("Confirmed by admin@acme.test", organization.VerificationEvidence);
        Assert.Equal(TestData.Now, organization.VerifiedAt);
        organization.EnsureCanIssueDocuments();
    }

    [Fact]
    public void A_domain_cannot_be_verified_twice()
    {
        var organization = TestData.VerifiedOrganization();

        var error = Assert.Throws<DomainException>(
            () => organization.MarkVerified(DomainVerificationMethod.DnsTxtRecord, "again", TestData.Now));

        Assert.Equal("organization.already_verified", error.Code);
    }

    [Fact]
    public void Only_the_exact_challenge_proves_the_domain()
    {
        var organization = TestData.PendingOrganization();

        Assert.True(organization.IsProvenBy(organization.DnsRecordValue));
        Assert.True(organization.IsProvenBy($"  {organization.DnsRecordValue} "));
        Assert.False(organization.IsProvenBy(organization.DnsRecordValue.ToUpperInvariant()));
        Assert.False(organization.IsProvenBy("chirograph-verification=" + TestData.PendingOrganization().DnsChallenge));
        Assert.False(organization.IsProvenBy("v=spf1 include:_spf.google.com ~all"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Administrator")]
    [InlineData("hostmaster")]
    [InlineData("postmaster")]
    [InlineData("webmaster")]
    public void Domain_confirmation_can_go_to_an_administrative_mailbox(string mailbox)
    {
        var email = TestData.PendingOrganization().DomainAdminMailbox(mailbox);

        Assert.Equal($"{mailbox.ToLowerInvariant()}@acme.test", email.Value);
    }

    [Theory]
    [InlineData("hr")]
    [InlineData("priya")]
    [InlineData("admin@evil.test")]
    public void Domain_confirmation_cannot_go_to_an_ordinary_mailbox(string mailbox)
    {
        var error = Assert.Throws<DomainException>(() => TestData.PendingOrganization().DomainAdminMailbox(mailbox));

        Assert.Equal("organization.invalid_admin_mailbox", error.Code);
    }

    [Theory]
    [InlineData("priya@hr.acme.test")]
    [InlineData("priya@acme.example")]
    public void Addresses_off_the_domain_are_not_the_organizations(string email)
    {
        var error = Assert.Throws<DomainException>(
            () => TestData.PendingOrganization().EnsureOwnsEmail(TestData.Email(email)));

        Assert.Equal("organization.email_not_on_domain", error.Code);
    }
}
