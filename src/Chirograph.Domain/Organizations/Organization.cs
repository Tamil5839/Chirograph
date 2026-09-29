using System.Security.Cryptography;
using Chirograph.Domain.Common;

namespace Chirograph.Domain.Organizations;

/// <summary>
/// A company that issues documents. The display <see cref="Name"/> is self-declared; the trust anchor is the
/// <see cref="Domain"/>, which must be proven before the organization may issue anything.
/// </summary>
public sealed class Organization
{
    public const int NameMaxLength = 200;
    public const int EvidenceMaxLength = 320;
    public const string DnsRecordLabel = "_chirograph";
    public const string DnsRecordPrefix = "chirograph-verification=";

    /// <summary>
    /// Mailboxes that conventionally belong to whoever administers a domain. This is the set certificate
    /// authorities accept for domain validation; an ordinary employee mailbox is deliberately not enough.
    /// </summary>
    public static IReadOnlyList<string> DomainAdminMailboxes { get; } =
        ["admin", "administrator", "hostmaster", "postmaster", "webmaster"];

    private Organization()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public DomainName Domain { get; private set; } = null!;

    public OrganizationStatus Status { get; private set; }

    /// <summary>Random value the organization publishes in DNS to prove control of <see cref="Domain"/>.</summary>
    public string DnsChallenge { get; private set; } = null!;

    public DomainVerificationMethod? VerificationMethod { get; private set; }

    /// <summary>Human-readable record of how the domain was proven, e.g. the TXT host or the confirming mailbox.</summary>
    public string? VerificationEvidence { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public bool IsVerified => Status == OrganizationStatus.Verified;

    /// <summary>Host name at which the TXT record is expected (the bare domain is accepted as well).</summary>
    public string DnsRecordName => $"{DnsRecordLabel}.{Domain.Value}";

    public string DnsRecordValue => DnsRecordPrefix + DnsChallenge;

    public static Organization Register(string name, DomainName domain, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var displayName = Text.Required(name, NameMaxLength, "organization name");
        if (FreeEmailProviders.Contains(domain))
        {
            throw new DomainException(
                "organization.public_email_domain",
                $"{domain} is a public email provider. Register your organization's own domain.");
        }

        return new Organization
        {
            Id = Guid.CreateVersion7(now),
            Name = displayName,
            Domain = domain,
            Status = OrganizationStatus.PendingVerification,
            DnsChallenge = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            CreatedAt = now,
        };
    }

    public bool IsProvenBy(string txtRecordValue) =>
        string.Equals(txtRecordValue.Trim(), DnsRecordValue, StringComparison.Ordinal);

    public void MarkVerified(DomainVerificationMethod method, string evidence, DateTimeOffset now)
    {
        if (IsVerified)
            throw new DomainException("organization.already_verified", "This organization's domain is already verified.");

        Status = OrganizationStatus.Verified;
        VerificationMethod = method;
        VerificationEvidence = Text.Required(evidence, EvidenceMaxLength, "verification evidence");
        VerifiedAt = now;
    }

    /// <summary>Builds the administrative mailbox (e.g. <c>admin@acme.com</c>) a domain confirmation may be sent to.</summary>
    public EmailAddress DomainAdminMailbox(string localPart)
    {
        var mailbox = localPart?.Trim().ToLowerInvariant();
        if (mailbox is null || !DomainAdminMailboxes.Contains(mailbox))
        {
            throw new DomainException(
                "organization.invalid_admin_mailbox",
                $"Choose one of: {string.Join(", ", DomainAdminMailboxes.Select(m => $"{m}@{Domain}"))}.");
        }
        return EmailAddress.Parse($"{mailbox}@{Domain.Value}");
    }

    public void EnsureOwnsEmail(EmailAddress email)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (!email.IsOn(Domain))
            throw new DomainException("organization.email_not_on_domain", $"Use an email address on {Domain}.");
    }

    public void EnsureCanIssueDocuments()
    {
        if (!IsVerified)
        {
            throw new DomainException(
                "organization.not_verified",
                "Verify your organization's domain before issuing documents.");
        }
    }
}
