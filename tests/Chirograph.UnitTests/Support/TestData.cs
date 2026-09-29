using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;
using Chirograph.Domain.Organizations;

namespace Chirograph.UnitTests.Support;

/// <summary>Builders for domain objects in a known-good state.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    public static DomainName Domain(string value = "acme.test") => DomainName.Parse(value);

    public static EmailAddress Email(string value) => EmailAddress.Parse(value);

    public static Organization PendingOrganization(string domain = "acme.test", string name = "Acme Technologies") =>
        Organization.Register(name, Domain(domain), Now);

    public static Organization VerifiedOrganization(string domain = "acme.test", string name = "Acme Technologies")
    {
        var organization = PendingOrganization(domain, name);
        organization.MarkVerified(DomainVerificationMethod.DnsTxtRecord, $"TXT record at _chirograph.{domain}", Now);
        return organization;
    }

    public static OrganizationMember ActiveAdmin(Organization organization, string localPart = "priya")
    {
        var admin = OrganizationMember.CreateFounder(organization, Email($"{localPart}@{organization.Domain}"), Now);
        admin.Activate(Now);
        return admin;
    }

    public static OrganizationMember ActiveIssuer(Organization organization, OrganizationMember admin, string localPart = "ravi")
    {
        var issuer = OrganizationMember.Invite(
            organization, admin, Email($"{localPart}@{organization.Domain}"), MemberRole.Issuer, Now);
        issuer.Activate(Now);
        return issuer;
    }

    public static DocumentDetails Details(
        DocumentType type = DocumentType.ExperienceLetter,
        string employeeName = "Anita Desai",
        string employeeEmail = "anita.desai@example.org",
        string designation = "Associate Engineer",
        DateOnly? start = null,
        DateOnly? end = null) =>
        DocumentDetails.Create(
            type,
            employeeName,
            Email(employeeEmail),
            designation,
            start ?? new DateOnly(2019, 6, 1),
            end ?? new DateOnly(2024, 3, 31));

    public static byte[] SampleFileBytes { get; } = "%PDF-1.7 sample content"u8.ToArray();

    public static IssuedDocument IssuedDocument(Organization organization, OrganizationMember issuedBy, byte[]? content = null)
    {
        content ??= SampleFileBytes;
        return Chirograph.Domain.Documents.IssuedDocument.Issue(
            organization,
            issuedBy,
            Details(),
            VerificationId.New(),
            DocumentFingerprint.Compute(content),
            content.Length,
            "documents/test.pdf",
            Now);
    }
}
